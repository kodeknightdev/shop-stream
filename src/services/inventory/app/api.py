from datetime import timedelta

from fastapi import APIRouter, Depends, Query, Request
from sqlalchemy import select
from sqlalchemy.orm import Session

from .auth import CurrentUser, require_admin
from .config import Settings, get_settings
from .db import get_session
from .models import Reservation, ReservationStatus, StockItem
from .schemas import (AdjustStockRequest, AvailabilityRequest, ReservationOut, StockItemAdmin, StockLevel, StockPage)
from .service import InventoryService

router = APIRouter(prefix="/api/inventory", tags=["inventory"])


def get_service(request: Request, session: Session = Depends(get_session), settings: Settings = Depends(get_settings)) -> InventoryService:
    return InventoryService(session, request.app.state.publisher, request.app.state.cache,
                            timedelta(seconds=settings.reservation_ttl_seconds))


def to_level(item: StockItem) -> StockLevel:
    return StockLevel(product_id=item.product_id, sku=item.sku, available=item.available, in_stock=item.available > 0, low_stock=item.low_stock)


def to_admin(item: StockItem) -> StockItemAdmin:
    return StockItemAdmin(**to_level(item).dict(), on_hand=item.on_hand, reserved=item.reserved,
                          low_stock_threshold=item.low_stock_threshold, updated_at=item.updated_at)


@router.get("", response_model=StockPage, summary="List stock levels (admin)")
def list_stock(
    low_stock: bool = Query(False, alias="lowStock"),
    page: int = Query(1, ge=1),
    page_size: int = Query(50, ge=1, le=200, alias="pageSize"),
    service: InventoryService = Depends(get_service),
    _: CurrentUser = Depends(require_admin),
):
    items, total = service.list_items(low_stock, page, page_size)
    return StockPage(items=[to_admin(i) for i in items], page=page, page_size=page_size, total=total)


@router.post("/availability", response_model=list[StockLevel], summary="Availability for several products")
def availability(body: AvailabilityRequest, session: Session = Depends(get_session)):
    ids = [str(p).lower() for p in body.product_ids]
    rows = session.scalars(select(StockItem).where(StockItem.product_id.in_(ids))).all()
    return [to_level(r) for r in rows]


@router.get("/reservations", response_model=list[ReservationOut], summary="Recent reservations (admin)")
def reservations(
    status: ReservationStatus | None = None,
    limit: int = Query(50, ge=1, le=200),
    session: Session = Depends(get_session),
    _: CurrentUser = Depends(require_admin),
):
    query = select(Reservation).order_by(Reservation.created_at.desc()).limit(limit)
    if status:
        query = query.where(Reservation.status == status)
    return [
        ReservationOut(id=r.id, order_id=r.order_id, status=r.status.value, expires_at=r.expires_at, created_at=r.created_at,
                       items=[{"product_id": i.product_id, "quantity": i.quantity} for i in r.items])
        for r in session.scalars(query).all()
    ]


@router.get("/{product_id}", response_model=StockLevel, summary="Public stock level for one product")
def get_stock(product_id: str, request: Request, service: InventoryService = Depends(get_service)):
    product_id = product_id.lower()
    cache = request.app.state.cache
    if cache:
        try:
            cached = cache.get_availability(product_id)
            if cached:
                return StockLevel(**cached)
        except Exception:
            pass
    level = to_level(service.get(product_id))
    if cache:
        try:
            cache.set_availability(product_id, level.dict())
        except Exception:
            pass
    return level


@router.post("/{product_id}/adjust", response_model=StockItemAdmin, summary="Adjust on-hand stock (admin)")
def adjust_stock(product_id: str, body: AdjustStockRequest, service: InventoryService = Depends(get_service),
                 user: CurrentUser = Depends(require_admin)):
    item = service.adjust(product_id, body.delta, f"{body.reason} (by {user.email})", body.sku, body.low_stock_threshold)
    return to_admin(item)
