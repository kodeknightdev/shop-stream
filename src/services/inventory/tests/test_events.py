import json
import uuid
from datetime import timedelta

import pytest

from app import events, retry
from app.handlers import make_handler
from app.models import ProcessedEvent, StockItem
from tests.conftest import order_placed


def envelope(payload, event_type="OrderPlaced"):
    return events.Envelope.parse(events.build(event_type, payload, "corr-1"))


def test_envelope_round_trip():
    raw = events.build("OrderPlaced", {"orderId": "abc"}, "corr-1")
    data = json.loads(raw)
    assert data["schemaVersion"] == 1
    assert data["correlationId"] == "corr-1"
    parsed = events.Envelope.parse(raw)
    assert parsed.payload == {"orderId": "abc"}


@pytest.mark.parametrize("raw", [b"not json", b"{}", b'{"eventId": "x"}', b"[]"])
def test_bad_envelopes_raise(raw):
    with pytest.raises((ValueError, TypeError)):
        events.Envelope.parse(raw)


def test_handler_skips_duplicate_event_ids_via_the_inbox_table(session, publisher, product_ids):
    handler = make_handler(publisher, None, timedelta(minutes=15))
    env = envelope(order_placed(str(uuid.uuid4()), (product_ids[0], 2)))

    handler(events.ORDER_PLACED, env)
    handler(events.ORDER_PLACED, env)

    session.expire_all()
    assert session.get(StockItem, product_ids[0]).reserved == 2
    assert len(publisher.of_type("StockReserved")) == 1
    row = session.get(ProcessedEvent, env.event_id)
    assert row is not None and row.event_type == "OrderPlaced"


def test_rejected_events_are_recorded_too(session, publisher, product_ids):
    handler = make_handler(publisher, None, timedelta(minutes=15))
    rejected = envelope(order_placed(str(uuid.uuid4()), (product_ids[2], 1)))  # sold out

    handler(events.ORDER_PLACED, rejected)

    assert session.get(ProcessedEvent, rejected.event_id) is not None
    assert len(publisher.of_type("StockRejected")) == 1


@pytest.mark.parametrize("previous,topic,delay_ms", [
    (0, "orders.order-placed.retry", 1000),
    (1, "orders.order-placed.retry", 2000),
    (2, "orders.order-placed.retry", 4000),
    (3, "orders.order-placed.dlq", None),
])
def test_retry_routing(previous, topic, delay_ms):
    decision = retry.on_failure("orders.order-placed", previous, max_retries=3, now_ms=1_000)
    assert decision.topic == topic
    assert decision.not_before_ms == (None if delay_ms is None else 1_000 + delay_ms)


def test_retry_topic_helpers():
    assert retry.source_topic("orders.order-cancelled.retry") == "orders.order-cancelled"
    assert not retry.is_retry_topic("orders.order-cancelled")
    assert retry.backoff_seconds(10) == retry.MAX_BACKOFF_SECONDS
