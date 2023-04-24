import json
import logging
import sys
from datetime import datetime, timezone

from .correlation import correlation_id


class JsonFormatter(logging.Formatter):
    def __init__(self, service: str):
        super().__init__()
        self.service = service

    def format(self, record: logging.LogRecord) -> str:
        payload = {
            "@t": datetime.fromtimestamp(record.created, tz=timezone.utc).isoformat(),
            "@l": record.levelname,
            "@m": record.getMessage(),
            "Service": self.service,
            "Logger": record.name,
            "CorrelationId": getattr(record, "correlation_id", None) or correlation_id.get(),
        }
        if record.exc_info:
            payload["@x"] = self.formatException(record.exc_info)
        return json.dumps(payload)


def configure_logging(service: str, fmt: str = "json") -> None:
    handler = logging.StreamHandler(sys.stdout)
    if fmt == "json":
        handler.setFormatter(JsonFormatter(service))
    else:
        handler.setFormatter(logging.Formatter("[%(asctime)s %(levelname)s] %(name)s: %(message)s"))
    root = logging.getLogger()
    root.handlers = [handler]
    root.setLevel(logging.INFO)
    logging.getLogger("uvicorn.access").setLevel(logging.WARNING)
