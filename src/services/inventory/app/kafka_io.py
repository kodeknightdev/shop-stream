import logging
import threading
import time
from collections.abc import Callable
from typing import Protocol

from confluent_kafka import Consumer, KafkaError, Producer
from confluent_kafka.admin import AdminClient

from . import events, retry
from .correlation import correlation_id, get_or_create

log = logging.getLogger(__name__)


class Publisher(Protocol):
    def publish(self, topic: str, key: str, event_type: str, payload: dict) -> None: ...


class NullPublisher:

    def publish(self, topic: str, key: str, event_type: str, payload: dict) -> None:
        log.info("Kafka disabled; would publish %s to %s", event_type, topic)


class KafkaPublisher:
    def __init__(self, bootstrap_servers: str):
        self._producer = Producer({"bootstrap.servers": bootstrap_servers, "enable.idempotence": True, "acks": "all"})

    def publish(self, topic: str, key: str, event_type: str, payload: dict) -> None:
        cid = get_or_create()
        errors: list[str] = []

        def on_delivery(err, msg):
            if err is not None:
                errors.append(str(err))

        self._producer.produce(
            topic,
            key=key.encode(),
            value=events.build(event_type, payload, cid).encode(),
            headers=[("correlation-id", cid.encode()), ("event-type", event_type.encode()), ("schema-version", b"1")],
            on_delivery=on_delivery,
        )
        remaining = self._producer.flush(10)
        if remaining or errors:
            raise RuntimeError(f"Failed to publish {event_type} to {topic}: {errors or 'timeout'}")
        log.info("Published %s to %s key %s", event_type, topic, key)

    def raw(self) -> Producer:
        return self._producer


def kafka_ping(bootstrap_servers: str, timeout: float = 3.0) -> bool:
    metadata = AdminClient({"bootstrap.servers": bootstrap_servers}).list_topics(timeout=timeout)
    return len(metadata.brokers) > 0


Handler = Callable[[str, events.Envelope], None]


class ConsumerLoop:

    def __init__(self, bootstrap_servers: str, group_id: str, topics: list[str], handler: Handler, max_retries: int = 3):
        self.group_id = group_id
        self.topics = topics
        self.handler = handler
        self.max_retries = max_retries
        self._config = {
            "bootstrap.servers": bootstrap_servers,
            "group.id": group_id,
            "enable.auto.commit": False,
            "auto.offset.reset": "earliest",
            "allow.auto.create.topics": False,
        }
        self._forwarder = Producer({"bootstrap.servers": bootstrap_servers, "acks": "all"})
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        self._thread = threading.Thread(target=self._run, name=f"{self.group_id}-consumer", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=10)

    def _run(self) -> None:
        consumer = Consumer(self._config)
        subscriptions = self.topics + [t + retry.RETRY_SUFFIX for t in self.topics]
        consumer.subscribe(subscriptions)
        log.info("Consumer %s subscribed to %s", self.group_id, ", ".join(subscriptions))
        try:
            while not self._stop.is_set():
                msg = consumer.poll(1.0)
                if msg is None:
                    continue
                if msg.error():
                    if msg.error().code() != KafkaError._PARTITION_EOF:
                        log.warning("Kafka error: %s", msg.error())
                    continue
                self.process(msg)
                consumer.commit(message=msg, asynchronous=False)
        except Exception:  # pragma: no cover
            log.exception("Consumer loop crashed")
        finally:
            consumer.close()

    def process(self, msg) -> None:
        headers = {k: (v.decode() if v else "") for k, v in (msg.headers() or [])}
        from_retry = retry.is_retry_topic(msg.topic())
        if from_retry and headers.get("x-consumer-group", self.group_id) != self.group_id:
            return  # another group's retry copy
        original_topic = headers.get("x-original-topic") or retry.source_topic(msg.topic())
        retries = int(headers.get("x-retry-count", "0") or 0)

        if from_retry and headers.get("x-not-before"):
            wait = int(headers["x-not-before"]) / 1000 - time.time()
            if wait > 0:
                time.sleep(min(wait, retry.MAX_BACKOFF_SECONDS))

        try:
            envelope = events.Envelope.parse(msg.value())
        except (ValueError, TypeError) as exc:
            dlq = original_topic + retry.DLQ_SUFFIX
            log.error("Poison message on %s; parking on %s", msg.topic(), dlq)
            self._forward(dlq, msg, original_topic, retries, str(exc), None)
            return

        token = correlation_id.set(headers.get("correlation-id") or envelope.correlation_id or envelope.event_id)
        try:
            self.handler(original_topic, envelope)
        except Exception as exc:
            decision = retry.on_failure(original_topic, retries, self.max_retries, int(time.time() * 1000))
            log.warning("Handler failed for %s %s (attempt %d): %s -> %s",
                        envelope.event_type, envelope.event_id, decision.attempt, exc, decision.topic)
            self._forward(decision.topic, msg, original_topic, decision.attempt, str(exc), decision.not_before_ms)
        finally:
            correlation_id.reset(token)

    def _forward(self, topic: str, msg, original_topic: str, retries: int, error: str, not_before_ms: int | None) -> None:
        replaced = ("x-retry-count", "x-original-topic", "x-error", "x-consumer-group", "x-not-before")
        keep = [(k, v) for k, v in (msg.headers() or []) if k not in replaced]
        keep += [("x-original-topic", original_topic.encode()), ("x-consumer-group", self.group_id.encode()),
                 ("x-retry-count", str(retries).encode()), ("x-error", error[:500].encode())]
        if not_before_ms is not None:
            keep.append(("x-not-before", str(not_before_ms).encode()))
        self._forwarder.produce(topic, key=msg.key(), value=msg.value(), headers=keep)
        self._forwarder.flush(10)
