import logging

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from .models import StockItem
from .seed_data import SEED_STOCK

log = logging.getLogger(__name__)


def seed(session: Session, low_stock_threshold: int = 5) -> int:
    if session.scalar(select(func.count()).select_from(StockItem)):
        return 0
    session.add_all(
        StockItem(product_id=pid, sku=sku, on_hand=qty, reserved=0, low_stock_threshold=low_stock_threshold)
        for pid, sku, qty in SEED_STOCK
    )
    session.commit()
    log.info("Seeded %d stock items", len(SEED_STOCK))
    return len(SEED_STOCK)
