import uuid
from datetime import datetime, timedelta, timezone

import pytest

from app.errors import Conflict, NotFound
from app.models import Reservation, ReservationStatus, StockItem
from app.service import InventoryService
from tests.conftest import order_placed


def service(session, publisher, cache=None):
    return InventoryService(session, publisher, cache, timedelta(minutes=15))


def test_reserve_holds_stock_and_publishes_reserved(session, publisher, product_ids):
    order_id = str(uuid.uuid4())
    outcome = service(session, publisher).reserve_for_order(order_placed(order_id, (product_ids[0], 3)))

    assert outcome.reserved
    item = session.get(StockItem, product_ids[0])
    assert (item.on_hand, item.reserved, item.available) == (10, 3, 7)
    [(topic, key, _, payload)] = publisher.of_type("StockReserved")
    assert topic == "inventory.stock-reserved"
    assert key == order_id
    assert payload["reservationId"] == outcome.reservation.id
    assert payload["paymentMethod"] == "pm_card_visa"


def test_reserve_is_all_or_nothing(session, publisher, product_ids):
    order_id = str(uuid.uuid4())
    outcome = service(session, publisher).reserve_for_order(order_placed(order_id, (product_ids[0], 1), (product_ids[1], 5)))

    assert not outcome.reserved
    assert "requested 5, available 2" in outcome.reason
    assert session.get(StockItem, product_ids[0]).reserved == 0
    assert publisher.of_type("StockReserved") == []
    assert publisher.of_type("StockRejected")[0][3]["orderId"] == order_id


def test_unknown_product_is_rejected(session, publisher, product_ids):
    outcome = service(session, publisher).reserve_for_order(order_placed(str(uuid.uuid4()), (str(uuid.uuid4()), 1)))
    assert not outcome.reserved


def test_redelivered_order_placed_does_not_double_reserve(session, publisher, product_ids):
    payload = order_placed(str(uuid.uuid4()), (product_ids[0], 4))
    svc = service(session, publisher)
    first = svc.reserve_for_order(payload)
    second = svc.reserve_for_order(payload)

    assert first.reservation.id == second.reservation.id
    assert session.get(StockItem, product_ids[0]).reserved == 4
    assert len(publisher.of_type("StockReserved")) == 2  # same answer published again


def test_release_returns_quantities(session, publisher, product_ids):
    order_id = str(uuid.uuid4())
    svc = service(session, publisher)
    svc.reserve_for_order(order_placed(order_id, (product_ids[0], 2), (product_ids[1], 2)))

    assert svc.release_for_order(order_id, "Payment failed")
    assert session.get(StockItem, product_ids[0]).reserved == 0
    assert session.get(StockItem, product_ids[1]).available == 2
    assert not svc.release_for_order(order_id, "again")  # idempotent


def test_commit_decrements_on_hand(session, publisher, product_ids):
    order_id = str(uuid.uuid4())
    svc = service(session, publisher)
    svc.reserve_for_order(order_placed(order_id, (product_ids[0], 3)))

    assert svc.commit_for_order(order_id)
    item = session.get(StockItem, product_ids[0])
    assert (item.on_hand, item.reserved) == (7, 0)
    assert not svc.release_for_order(order_id, "too late")


def test_expired_reservations_are_released_and_rejected(session, publisher, product_ids):
    order_id = str(uuid.uuid4())
    svc = service(session, publisher)
    svc.reserve_for_order(order_placed(order_id, (product_ids[0], 5)))

    count = svc.expire_reservations(now=datetime.now(timezone.utc) + timedelta(minutes=16))

    assert count == 1
    assert session.get(StockItem, product_ids[0]).reserved == 0
    reservation = session.query(Reservation).filter_by(order_id=order_id).one()
    assert reservation.status == ReservationStatus.EXPIRED


def test_adjust_cannot_drop_below_reserved(session, publisher, product_ids):
    svc = service(session, publisher)
    svc.reserve_for_order(order_placed(str(uuid.uuid4()), (product_ids[0], 8)))
    with pytest.raises(Conflict):
        svc.adjust(product_ids[0], -5, "shrinkage")
    assert svc.adjust(product_ids[0], -2, "shrinkage").on_hand == 8


def test_adjust_creates_record_when_sku_given(session, publisher):
    pid = str(uuid.uuid4())
    svc = service(session, publisher)
    with pytest.raises(NotFound):
        svc.adjust(pid, 5, "first delivery")
    item = svc.adjust(pid, 5, "first delivery", sku="NEW-1")
    assert item.on_hand == 5 and item.sku == "NEW-1"


def test_low_stock_flag(session, product_ids):
    assert session.get(StockItem, product_ids[1]).low_stock
    assert not session.get(StockItem, product_ids[0]).low_stock
