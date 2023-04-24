import logging

from fastapi import FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from .correlation import correlation_id

log = logging.getLogger(__name__)
PROBLEM = "application/problem+json"


class InventoryError(Exception):
    status = 400
    title = "Bad request"

    def __init__(self, detail: str):
        super().__init__(detail)
        self.detail = detail


class NotFound(InventoryError):
    status = 404
    title = "Resource not found"


class Conflict(InventoryError):
    status = 409
    title = "Conflict"


def problem(request: Request, status: int, title: str, detail: str | None = None, **extra) -> JSONResponse:
    body = {
        "type": f"https://httpstatuses.io/{status}",
        "title": title,
        "status": status,
        "instance": request.url.path,
        "correlationId": correlation_id.get(),
    }
    if detail:
        body["detail"] = detail
    body.update(extra)
    return JSONResponse(body, status_code=status, media_type=PROBLEM)


def register_error_handlers(app: FastAPI) -> None:
    @app.exception_handler(InventoryError)
    async def _inventory_error(request: Request, exc: InventoryError):
        return problem(request, exc.status, exc.title, exc.detail)

    @app.exception_handler(HTTPException)
    async def _http_error(request: Request, exc: HTTPException):
        return problem(request, exc.status_code, str(exc.detail) if exc.status_code < 500 else "Server error")

    @app.exception_handler(RequestValidationError)
    async def _validation_error(request: Request, exc: RequestValidationError):
        errors: dict[str, list[str]] = {}
        for err in exc.errors():
            key = ".".join(str(p) for p in err["loc"] if p not in ("body", "query", "path"))
            errors.setdefault(key or "request", []).append(err["msg"])
        return problem(request, 400, "One or more validation errors occurred.", errors=errors)

    @app.exception_handler(Exception)
    async def _unhandled(request: Request, exc: Exception):
        log.exception("Unhandled error on %s", request.url.path)
        return problem(request, 500, "An unexpected error occurred")
