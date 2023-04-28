import json

import redis

AVAILABILITY_TTL_SECONDS = 15


class InventoryCache:
    def __init__(self, client: redis.Redis, prefix: str = "inventory"):
        self.client = client
        self.prefix = prefix

    @classmethod
    def from_url(cls, url: str) -> "InventoryCache":
        return cls(redis.Redis.from_url(url, decode_responses=True, socket_timeout=2))

    def get_availability(self, product_id: str) -> dict | None:
        raw = self.client.get(f"{self.prefix}:availability:{product_id}")
        return json.loads(raw) if raw else None

    def set_availability(self, product_id: str, value: dict) -> None:
        self.client.set(f"{self.prefix}:availability:{product_id}", json.dumps(value), ex=AVAILABILITY_TTL_SECONDS)

    def invalidate(self, *product_ids: str) -> None:
        if product_ids:
            self.client.delete(*[f"{self.prefix}:availability:{p}" for p in product_ids])

    def ping(self) -> bool:
        return bool(self.client.ping())
