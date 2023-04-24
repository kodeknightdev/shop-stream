from functools import lru_cache

from pydantic import BaseSettings


class Settings(BaseSettings):
    service_name: str = "inventory"
    database_url: str = "postgresql+psycopg2://shopstream:shopstream@localhost:5432/inventory_db"
    redis_url: str = "redis://localhost:6379/0"

    kafka_enabled: bool = True
    kafka_bootstrap_servers: str = "localhost:9094"
    kafka_group_id: str = "inventory"
    kafka_max_retries: int = 3

    jwt_signing_key: str = ""
    jwt_issuer: str = "shop-stream-identity"
    jwt_audience: str = "shop-stream"

    reservation_ttl_seconds: int = 900
    expiry_interval_seconds: int = 30
    low_stock_threshold: int = 5
    seed_on_startup: bool = True
    log_format: str = "json"

    class Config:
        env_file = ".env"


@lru_cache
def get_settings() -> Settings:
    return Settings()
