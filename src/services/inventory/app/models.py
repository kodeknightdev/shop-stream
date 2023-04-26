import enum
import uuid
from datetime import datetime, timezone

from sqlalchemy import DateTime, Enum, ForeignKey, Integer, String, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column, relationship

from .db import Base


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


class ReservationStatus(str, enum.Enum):
    RESERVED = "Reserved"
    COMMITTED = "Committed"
    RELEASED = "Released"
    EXPIRED = "Expired"


class StockItem(Base):
    __tablename__ = "stock_items"

    product_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    sku: Mapped[str] = mapped_column(String(32), index=True)
    on_hand: Mapped[int] = mapped_column(Integer, default=0)
    reserved: Mapped[int] = mapped_column(Integer, default=0)
    low_stock_threshold: Mapped[int] = mapped_column(Integer, default=5)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow, onupdate=utcnow)

    @property
    def available(self) -> int:
        return max(0, self.on_hand - self.reserved)

    @property
    def low_stock(self) -> bool:
        return self.available <= self.low_stock_threshold


class Reservation(Base):
    __tablename__ = "reservations"
    __table_args__ = (UniqueConstraint("order_id", name="uq_reservations_order_id"),)

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    order_id: Mapped[str] = mapped_column(String(36), index=True)
    status: Mapped[ReservationStatus] = mapped_column(Enum(ReservationStatus, native_enum=False, length=16), default=ReservationStatus.RESERVED)
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), index=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow, onupdate=utcnow)

    items: Mapped[list["ReservationItem"]] = relationship(back_populates="reservation", cascade="all, delete-orphan", lazy="selectin")


class ReservationItem(Base):
    __tablename__ = "reservation_items"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    reservation_id: Mapped[str] = mapped_column(ForeignKey("reservations.id", ondelete="CASCADE"), index=True)
    product_id: Mapped[str] = mapped_column(String(36))
    quantity: Mapped[int] = mapped_column(Integer)

    reservation: Mapped[Reservation] = relationship(back_populates="items")


class StockAdjustment(Base):
    __tablename__ = "stock_adjustments"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    product_id: Mapped[str] = mapped_column(String(36), index=True)
    delta: Mapped[int] = mapped_column(Integer)
    reason: Mapped[str] = mapped_column(String(200))
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)


class ProcessedEvent(Base):

    __tablename__ = "processed_events"

    event_id: Mapped[str] = mapped_column(String(64), primary_key=True)
    event_type: Mapped[str] = mapped_column(String(100))
    processed_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=utcnow)
