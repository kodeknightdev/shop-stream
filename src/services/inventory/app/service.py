import logging
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from . import events
from .cache import InventoryCache
from .errors import Conflict, NotFound
from .kafka_io import Publisher
from .models import ProcessedEvent, Reservation, ReservationItem, ReservationStatus, StockAdjustment, StockItem

log = logging.getLogger(__name__)


@dataclass
class ReserveOutcome:
    reserved: bool
    reservation: Reservation | None = None
    reason: str | None = None


def _aware(value: datetime) -> datetime:
    # SQLite hands back naive datetimes; Postgres keeps the tz.
    return value if value.tzinfo else value.replace(tzinfo=timezone.utc)


class InventoryService:
    def __init__(self, session: Session, publisher: Publisher, cache: InventoryCache | None = None,
                 reservation_ttl: timedelta = timedelta(minutes=15), event: tuple[str, str] | None = None):
        self.session = session
        self.publisher = publisher
        self.cache = cache
        self.reservation_ttl = reservation_ttl
        # (event_id, event_type) being handled, goes into the inbox on commit
        self.event = event

    # --- queries -----------------------------------------------------------------

    def get(self, product_id: str) -> StockItem:
        item = self.session.get(StockItem, product_id)
        if item is None:
            raise NotFound(f"No stock record for product {product_id}")
        return item

    def list_items(self, low_stock_only: bool, page: int, page_size: int) -> tuple[list[StockItem], int]:
        query = select(StockItem)
        if low_stock_only:
            query = query.where((StockItem.on_hand - StockItem.reserved) <= StockItem.low_stock_threshold)
        total = self.session.scalar(select(func.count()).select_from(query.subquery())) or 0
        rows = self.session.scalars(query.order_by(StockItem.sku).offset((page - 1) * page_size).limit(page_size)).all()
        return list(rows), total

    # --- saga steps --------------------------------------------------------------

    def reserve_for_order(self, payload: dict) -> ReserveOutcome:
        order_id = str(payload["orderId"])
        existing = self.session.scalar(select(Reservation).where(Reservation.order_id == order_id))
        if existing is not None:
            # redelivery: same answer as last time
            log.info("Order %s already has reservation %s (%s)", order_id, existing.id, existing.status.value)
            self._commit()
            if existing.status in (ReservationStatus.RESERVED, ReservationStatus.COMMITTED):
                self._publish_reserved(payload, existing)
                return ReserveOutcome(True, existing)
            self._publish_rejected(order_id, "Reservation is no longer active")
            return ReserveOutcome(False, existing, "Reservation is no longer active")

        wanted: dict[str, int] = {}
        for line in payload.get("items", []):
            wanted[str(line["productId"]).lower()] = wanted.get(str(line["productId"]).lower(), 0) + int(line["quantity"])
        names = {str(line["productId"]).lower(): line.get("sku") or line.get("name") for line in payload.get("items", [])}

        rows = self.session.scalars(
            select(StockItem).where(StockItem.product_id.in_(list(wanted))).order_by(StockItem.product_id).with_for_update()
        ).all()
        stock = {row.product_id: row for row in rows}

        shortages = []
        for product_id, quantity in wanted.items():
            row = stock.get(product_id)
            available = row.available if row else 0
            if available < quantity:
                shortages.append(f"{names.get(product_id, product_id)} (requested {quantity}, available {available})")

        if shortages:
            self.session.rollback()
            self._commit()  # still record the event
            reason = "Insufficient stock: " + ", ".join(shortages)
            self._publish_rejected(order_id, reason)
            return ReserveOutcome(False, None, reason)

        reservation = Reservation(
            order_id=order_id,
            status=ReservationStatus.RESERVED,
            expires_at=datetime.now(timezone.utc) + self.reservation_ttl,
            items=[ReservationItem(product_id=p, quantity=q) for p, q in wanted.items()],
        )
        for product_id, quantity in wanted.items():
            stock[product_id].reserved += quantity
        self.session.add(reservation)
        self._commit()
        self._invalidate(*wanted)

        log.info("Reserved stock for order %s (reservation %s)", order_id, reservation.id)
        self._publish_reserved(payload, reservation)
        return ReserveOutcome(True, reservation)

    def release_for_order(self, order_id: str, reason: str, status: ReservationStatus = ReservationStatus.RELEASED) -> bool:
        reservation = self.session.scalar(select(Reservation).where(Reservation.order_id == str(order_id)).with_for_update())
        if reservation is None or reservation.status != ReservationStatus.RESERVED:
            self._commit()
            return False
        self._return_quantities(reservation)
        reservation.status = status
        self._commit()
        self._invalidate(*[i.product_id for i in reservation.items])
        log.info("Released reservation %s for order %s: %s", reservation.id, order_id, reason)
        return True

    def commit_for_order(self, order_id: str) -> bool:
        # paid - the units are gone for good
        reservation = self.session.scalar(select(Reservation).where(Reservation.order_id == str(order_id)).with_for_update())
        if reservation is None or reservation.status != ReservationStatus.RESERVED:
            self._commit()
            return False
        rows = self._lock_rows([i.product_id for i in reservation.items])
        for item in reservation.items:
            row = rows[item.product_id]
            row.reserved = max(0, row.reserved - item.quantity)
            row.on_hand = max(0, row.on_hand - item.quantity)
        reservation.status = ReservationStatus.COMMITTED
        self._commit()
        self._invalidate(*rows)
        return True

    def expire_reservations(self, now: datetime | None = None) -> int:
        now = now or datetime.now(timezone.utc)
        expired = self.session.scalars(
            select(Reservation).where(Reservation.status == ReservationStatus.RESERVED, Reservation.expires_at < now).with_for_update(skip_locked=True)
        ).all()
        for reservation in expired:
            self._return_quantities(reservation)
            reservation.status = ReservationStatus.EXPIRED
        self.session.commit()
        for reservation in expired:
            self._invalidate(*[i.product_id for i in reservation.items])
        return len(expired)

    # --- admin -------------------------------------------------------------------

    def adjust(self, product_id: str, delta: int, reason: str, sku: str | None = None, low_stock_threshold: int | None = None) -> StockItem:
        product_id = product_id.lower()
        item = self.session.get(StockItem, product_id, with_for_update=True)
        if item is None:
            if not sku:
                raise NotFound(f"No stock record for product {product_id}; pass a sku to create one")
            if delta < 0:
                raise Conflict("Cannot create a stock record with a negative quantity")
            item = StockItem(product_id=product_id, sku=sku, on_hand=0, reserved=0, low_stock_threshold=low_stock_threshold or 5)
            self.session.add(item)
        if item.on_hand + delta < item.reserved:
            raise Conflict(f"On-hand stock cannot drop below the {item.reserved} units currently reserved")
        item.on_hand += delta
        if low_stock_threshold is not None:
            item.low_stock_threshold = low_stock_threshold
        self.session.add(StockAdjustment(product_id=product_id, delta=delta, reason=reason))
        self.session.commit()
        self._invalidate(product_id)
        return item

    # --- helpers -----------------------------------------------------------------

    def _commit(self) -> None:
        if self.event is not None and self.session.get(ProcessedEvent, self.event[0]) is None:
            self.session.add(ProcessedEvent(event_id=self.event[0], event_type=self.event[1]))
        self.session.commit()

    def _lock_rows(self, product_ids: list[str]) -> dict[str, StockItem]:
        rows = self.session.scalars(select(StockItem).where(StockItem.product_id.in_(product_ids)).order_by(StockItem.product_id).with_for_update()).all()
        return {r.product_id: r for r in rows}

    def _return_quantities(self, reservation: Reservation) -> None:
        rows = self._lock_rows([i.product_id for i in reservation.items])
        for item in reservation.items:
            if item.product_id in rows:
                rows[item.product_id].reserved = max(0, rows[item.product_id].reserved - item.quantity)

    def _invalidate(self, *product_ids: str) -> None:
        if self.cache:
            try:
                self.cache.invalidate(*product_ids)
            except Exception:  # cache is best-effort
                log.warning("Could not invalidate availability cache", exc_info=True)

    def _publish_reserved(self, payload: dict, reservation: Reservation) -> None:
        self.publisher.publish(events.STOCK_RESERVED, str(payload["orderId"]), "StockReserved", {
            "orderId": str(payload["orderId"]),
            "reservationId": reservation.id,
            "userId": payload.get("userId"),
            "email": payload.get("email"),
            "total": payload.get("total"),
            "currency": payload.get("currency", "USD"),
            "paymentMethod": payload.get("paymentMethod"),
            "expiresAt": _aware(reservation.expires_at).isoformat(),
        })

    def _publish_rejected(self, order_id: str, reason: str) -> None:
        self.publisher.publish(events.STOCK_REJECTED, order_id, "StockRejected", {"orderId": order_id, "reason": reason})
