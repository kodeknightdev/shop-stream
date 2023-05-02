import logging

from . import events
from .cache import InventoryCache
from .db import session_factory
from .kafka_io import Publisher
from .models import ProcessedEvent
from .service import InventoryService

log = logging.getLogger(__name__)

TOPICS = [events.ORDER_PLACED, events.PAYMENT_SUCCEEDED]


def make_handler(publisher: Publisher, cache: InventoryCache | None, reservation_ttl):
    def handle(topic: str, envelope: events.Envelope) -> None:
        with session_factory()() as session:
            # inbox row is written together with the stock change (_commit)
            if session.get(ProcessedEvent, envelope.event_id) is not None:
                log.info("Skipping duplicate %s %s", envelope.event_type, envelope.event_id)
                return

            service = InventoryService(session, publisher, cache, reservation_ttl, event=(envelope.event_id, envelope.event_type))
            payload = envelope.payload
            if topic == events.ORDER_PLACED:
                service.reserve_for_order(payload)
            elif topic == events.PAYMENT_SUCCEEDED:
                service.commit_for_order(str(payload["orderId"]))
            else:
                log.debug("Ignoring topic %s", topic)

    return handle
