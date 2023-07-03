'use strict';

// same routing as the .NET / python consumers
const RETRY_SUFFIX = '.retry';
const DLQ_SUFFIX = '.dlq';
const MAX_BACKOFF_MS = 30_000;

const backoffMs = (attempt) => Math.min(MAX_BACKOFF_MS, 1000 * 2 ** Math.max(0, attempt - 1));

function onFailure(originalTopic, previousAttempts, maxRetries, nowMs) {
  const attempt = previousAttempts + 1;
  if (attempt > maxRetries) return { topic: originalTopic + DLQ_SUFFIX, attempt, notBeforeMs: null };
  return { topic: originalTopic + RETRY_SUFFIX, attempt, notBeforeMs: nowMs + backoffMs(attempt) };
}

const isRetryTopic = (topic) => topic.endsWith(RETRY_SUFFIX);
const sourceTopic = (topic) => (isRetryTopic(topic) ? topic.slice(0, -RETRY_SUFFIX.length) : topic);

module.exports = { RETRY_SUFFIX, DLQ_SUFFIX, MAX_BACKOFF_MS, backoffMs, onFailure, isRetryTopic, sourceTopic };
