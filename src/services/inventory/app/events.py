import json
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any

ORDER_PLACED = "orders.order-placed"
ORDER_CANCELLED = "orders.order-cancelled"
PAYMENT_SUCCEEDED = "payments.payment-succeeded"
STOCK_RESERVED = "inventory.stock-reserved"
STOCK_REJECTED = "inventory.stock-rejected"

SCHEMA_VERSION = 1


@dataclass(frozen=True)
class Envelope:
    event_id: str
    event_type: str
    schema_version: int
    occurred_at: str
    correlation_id: str | None
    payload: dict[str, Any]

    @classmethod
    def parse(cls, raw: bytes | str) -> "Envelope":
        data = json.loads(raw)
        if not isinstance(data, dict) or not data.get("eventId") or "payload" not in data:
            raise ValueError("Message is not a shop-stream event envelope")
        return cls(
            event_id=str(data["eventId"]),
            event_type=str(data.get("eventType", "")),
            schema_version=int(data.get("schemaVersion", 1)),
            occurred_at=str(data.get("occurredAt", "")),
            correlation_id=data.get("correlationId"),
            payload=data["payload"],
        )


def build(event_type: str, payload: dict[str, Any], correlation_id: str | None) -> str:
    return json.dumps(
        {
            "eventId": str(uuid.uuid4()),
            "eventType": event_type,
            "schemaVersion": SCHEMA_VERSION,
            "occurredAt": datetime.now(timezone.utc).isoformat(),
            "correlationId": correlation_id,
            "payload": payload,
        },
        default=str,
    )
