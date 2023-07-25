// same routing as the other consumers
export const RETRY_SUFFIX = '.retry';
export const DLQ_SUFFIX = '.dlq';
export const MAX_BACKOFF_MS = 30_000;

export const backoffMs = (attempt: number) => Math.min(MAX_BACKOFF_MS, 1000 * 2 ** Math.max(0, attempt - 1));

export function onFailure(originalTopic: string, previousAttempts: number, maxRetries: number, nowMs: number) {
  const attempt = previousAttempts + 1;
  return attempt > maxRetries
    ? { topic: originalTopic + DLQ_SUFFIX, attempt, notBeforeMs: null as number | null }
    : { topic: originalTopic + RETRY_SUFFIX, attempt, notBeforeMs: nowMs + backoffMs(attempt) as number | null };
}

export const isRetryTopic = (topic: string) => topic.endsWith(RETRY_SUFFIX);
export const sourceTopic = (topic: string) => (isRetryTopic(topic) ? topic.slice(0, -RETRY_SUFFIX.length) : topic);
