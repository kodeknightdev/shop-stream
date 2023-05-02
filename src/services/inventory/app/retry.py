# same routing as the .NET consumers
from dataclasses import dataclass

RETRY_SUFFIX = ".retry"
DLQ_SUFFIX = ".dlq"
MAX_BACKOFF_SECONDS = 30


@dataclass(frozen=True)
class RetryDecision:
    topic: str
    attempt: int
    not_before_ms: int | None


def backoff_seconds(attempt: int) -> float:
    return float(min(MAX_BACKOFF_SECONDS, 2 ** max(0, attempt - 1)))


def on_failure(original_topic: str, previous_attempts: int, max_retries: int, now_ms: int) -> RetryDecision:
    attempt = previous_attempts + 1
    if attempt > max_retries:
        return RetryDecision(original_topic + DLQ_SUFFIX, attempt, None)
    return RetryDecision(original_topic + RETRY_SUFFIX, attempt, now_ms + int(backoff_seconds(attempt) * 1000))


def is_retry_topic(topic: str) -> bool:
    return topic.endswith(RETRY_SUFFIX)


def source_topic(topic: str) -> str:
    return topic[: -len(RETRY_SUFFIX)] if is_retry_topic(topic) else topic
