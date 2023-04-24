import uuid
from contextvars import ContextVar

from starlette.middleware.base import BaseHTTPMiddleware
from starlette.requests import Request

HEADER = "X-Correlation-ID"
correlation_id: ContextVar[str | None] = ContextVar("correlation_id", default=None)


def get_or_create() -> str:
    value = correlation_id.get()
    if not value:
        value = uuid.uuid4().hex
        correlation_id.set(value)
    return value


class CorrelationIdMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request: Request, call_next):
        value = request.headers.get(HEADER) or uuid.uuid4().hex
        token = correlation_id.set(value)
        try:
            response = await call_next(request)
        finally:
            correlation_id.reset(token)
        response.headers[HEADER] = value
        return response
