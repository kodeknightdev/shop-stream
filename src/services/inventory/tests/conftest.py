import os
import time
import uuid

import fakeredis
import jwt
import pytest
from sqlalchemy.pool import StaticPool

os.environ.update({
    "DATABASE_URL": "sqlite+pysqlite://",
    "JWT_SIGNING_KEY": "test-signing-key-for-inventory-tests-000000",
    "KAFKA_ENABLED": "false",
    "REDIS_URL": "",
    "SEED_ON_STARTUP": "false",
    "LOG_FORMAT": "text",
})

from app import db  # noqa: E402
from app.cache import InventoryCache  # noqa: E402
from app.config import get_settings  # noqa: E402
from app.models import StockItem  # noqa: E402


class RecordingPublisher:
    def __init__(self):
        self.messages: list[tuple[str, str, str, dict]] = []

    def publish(self, topic, key, event_type, payload):
        self.messages.append((topic, key, event_type, payload))

    def of_type(self, event_type):
        return [m for m in self.messages if m[2] == event_type]


@pytest.fixture()
def engine():
    get_settings.cache_clear()
    engine = db.init_engine("sqlite+pysqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(engine)
    yield engine
    db.Base.metadata.drop_all(engine)


@pytest.fixture()
def session(engine):
    with db.session_factory()() as s:
        yield s


@pytest.fixture()
def publisher():
    return RecordingPublisher()


@pytest.fixture()
def cache():
    return InventoryCache(fakeredis.FakeRedis(decode_responses=True))


@pytest.fixture()
def product_ids(session):
    ids = [str(uuid.uuid4()) for _ in range(3)]
    session.add_all([
        StockItem(product_id=ids[0], sku="ELE-1001", on_hand=10, reserved=0, low_stock_threshold=3),
        StockItem(product_id=ids[1], sku="BOO-1002", on_hand=2, reserved=0, low_stock_threshold=3),
        StockItem(product_id=ids[2], sku="TOY-1003", on_hand=0, reserved=0, low_stock_threshold=3),
    ])
    session.commit()
    return ids


def make_token(role="Customer", sub=None, email="casey@example.com", expires_in=600):
    now = int(time.time())
    return jwt.encode(
        {"sub": sub or str(uuid.uuid4()), "email": email, "role": role, "iss": "shop-stream-identity",
         "aud": "shop-stream", "iat": now, "nbf": now, "exp": now + expires_in},
        os.environ["JWT_SIGNING_KEY"],
        algorithm="HS256",
    )


def order_placed(order_id, *lines):
    return {
        "orderId": order_id,
        "userId": str(uuid.uuid4()),
        "email": "casey@example.com",
        "items": [{"productId": pid, "sku": f"SKU-{i}", "name": f"Item {i}", "quantity": q, "unitPrice": 10.0} for i, (pid, q) in enumerate(lines)],
        "total": 10.0 * sum(q for _, q in lines),
        "currency": "USD",
        "paymentMethod": "pm_card_visa",
    }
