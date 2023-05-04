import asyncio
import contextlib
import logging
from datetime import timedelta

from fastapi import FastAPI
from fastapi.responses import JSONResponse
from sqlalchemy import text

from .api import router
from .cache import InventoryCache
from .config import get_settings
from .correlation import CorrelationIdMiddleware
from .db import Base, get_engine, session_factory
from .errors import register_error_handlers
from .handlers import TOPICS, make_handler
from .kafka_io import ConsumerLoop, KafkaPublisher, NullPublisher, kafka_ping
from .logging_config import configure_logging
from .seed import seed
from .service import InventoryService

log = logging.getLogger("inventory")


async def expiry_loop(app: FastAPI, interval: int, ttl: timedelta) -> None:
    while True:
        await asyncio.sleep(interval)
        try:
            with session_factory()() as session:
                count = await asyncio.to_thread(InventoryService(session, app.state.publisher, app.state.cache, ttl).expire_reservations)
            if count:
                log.info("Expired %d reservation(s)", count)
        except Exception:
            log.exception("Reservation expiry sweep failed")


def create_app(start_background: bool = True) -> FastAPI:
    settings = get_settings()
    configure_logging(settings.service_name, settings.log_format)
    if not settings.jwt_signing_key:
        raise RuntimeError("JWT_SIGNING_KEY must be set")

    app = FastAPI(title="shop-stream inventory", version="1.0.0", description="Stock levels and reservations")
    app.add_middleware(CorrelationIdMiddleware)
    register_error_handlers(app)
    app.include_router(router)

    app.state.publisher = NullPublisher()
    app.state.cache = None
    app.state.consumer = None

    @app.on_event("startup")
    async def startup() -> None:
        Base.metadata.create_all(get_engine())
        if settings.seed_on_startup:
            with session_factory()() as session:
                seed(session, settings.low_stock_threshold)

        if app.state.cache is None and settings.redis_url:
            app.state.cache = InventoryCache.from_url(settings.redis_url)

        if not start_background:
            return

        ttl = timedelta(seconds=settings.reservation_ttl_seconds)
        if settings.kafka_enabled:
            app.state.publisher = KafkaPublisher(settings.kafka_bootstrap_servers)
            consumer = ConsumerLoop(settings.kafka_bootstrap_servers, settings.kafka_group_id, TOPICS,
                                    make_handler(app.state.publisher, app.state.cache, ttl), settings.kafka_max_retries)
            consumer.start()
            app.state.consumer = consumer
        app.state.expiry_task = asyncio.create_task(expiry_loop(app, settings.expiry_interval_seconds, ttl))

    @app.on_event("shutdown")
    async def shutdown() -> None:
        if app.state.consumer:
            app.state.consumer.stop()
        task = getattr(app.state, "expiry_task", None)
        if task:
            task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await task

    @app.get("/health", tags=["health"])
    def health():
        return {"status": "Healthy"}

    @app.get("/health/ready", tags=["health"])
    def ready():
        checks = {}
        try:
            with get_engine().connect() as conn:
                conn.execute(text("SELECT 1"))
            checks["postgres"] = "Healthy"
        except Exception as exc:
            checks["postgres"] = f"Unhealthy: {exc.__class__.__name__}"
        try:
            checks["redis"] = "Healthy" if app.state.cache and app.state.cache.ping() else "Unhealthy"
        except Exception as exc:
            checks["redis"] = f"Unhealthy: {exc.__class__.__name__}"
        if settings.kafka_enabled:
            try:
                checks["kafka"] = "Healthy" if kafka_ping(settings.kafka_bootstrap_servers) else "Unhealthy"
            except Exception as exc:
                checks["kafka"] = f"Unhealthy: {exc.__class__.__name__}"
        healthy = all(v == "Healthy" for v in checks.values())
        return JSONResponse({"status": "Healthy" if healthy else "Unhealthy", "checks": checks}, status_code=200 if healthy else 503)

    return app

