from datetime import datetime
from uuid import UUID

from pydantic import BaseModel, Field, validator


def to_camel(value: str) -> str:
    head, *tail = value.split("_")
    return head + "".join(part.title() for part in tail)


class CamelModel(BaseModel):
    class Config:
        alias_generator = to_camel
        allow_population_by_field_name = True
        orm_mode = True


class StockLevel(CamelModel):
    product_id: str
    sku: str
    available: int
    in_stock: bool
    low_stock: bool


class StockItemAdmin(StockLevel):
    on_hand: int
    reserved: int
    low_stock_threshold: int
    updated_at: datetime


class StockPage(CamelModel):
    items: list[StockItemAdmin]
    page: int
    page_size: int
    total: int


class AvailabilityRequest(CamelModel):
    product_ids: list[UUID] = Field(..., min_items=1, max_items=100)


class AdjustStockRequest(CamelModel):
    delta: int = Field(..., ge=-10_000, le=10_000)
    reason: str = Field(..., min_length=3, max_length=200)
    sku: str | None = Field(None, max_length=32)
    low_stock_threshold: int | None = Field(None, ge=0, le=1000)

    @validator("delta")
    def delta_not_zero(cls, v: int) -> int:
        if v == 0:
            raise ValueError("delta must not be zero")
        return v


class ReservationItemOut(CamelModel):
    product_id: str
    quantity: int


class ReservationOut(CamelModel):
    id: str
    order_id: str
    status: str
    expires_at: datetime
    created_at: datetime
    items: list[ReservationItemOut]
