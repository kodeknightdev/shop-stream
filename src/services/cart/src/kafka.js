'use strict';

const { Kafka, logLevel } = require('kafkajs');
const logger = require('./logger');
const retry = require('./retry');
const { runWithCorrelationId } = require('./correlation');

const PRICE_CHANGED = 'catalog.product-price-changed';

function parseEnvelope(buffer) {
  const data = JSON.parse(buffer.toString());
  if (!data || typeof data !== 'object' || !data.eventId || !data.payload) {
    throw new Error('Not a shop-stream envelope');
  }
  return data;
}

function headerValue(headers, key) {
  const v = headers?.[key];
  return v === undefined ? undefined : v.toString();
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// no kafkajs in here, makes it easy to test
function createMessageHandler({ groupId, maxRetries, store, inbox, forward, now = Date.now, wait = sleep }) {
  return async (topic, message) => {
    const fromRetry = retry.isRetryTopic(topic);
    const owner = headerValue(message.headers, 'x-consumer-group');
    if (fromRetry && owner && owner !== groupId) return 'skipped';

    const originalTopic = headerValue(message.headers, 'x-original-topic') || retry.sourceTopic(topic);
    const retries = Number(headerValue(message.headers, 'x-retry-count') || 0);
    const notBefore = Number(headerValue(message.headers, 'x-not-before') || 0);
    if (fromRetry && notBefore > now()) await wait(Math.min(notBefore - now(), retry.MAX_BACKOFF_MS));

    let envelope;
    try {
      envelope = parseEnvelope(message.value);
    } catch (err) {
      logger.error({ err, topic }, 'poison message, parking on dlq');
      await forward(originalTopic + retry.DLQ_SUFFIX, message, { originalTopic, retries, error: err.message });
      return 'dead-lettered';
    }

    const correlationId = headerValue(message.headers, 'correlation-id') || envelope.correlationId;
    return runWithCorrelationId(correlationId, async () => {
      try {
        if (await inbox.seen(envelope.eventId)) return 'duplicate';
        if (originalTopic === PRICE_CHANGED) {
          const { productId, newPrice } = envelope.payload;
          const flagged = await store.flagPriceChange(String(productId).toLowerCase(), Number(newPrice));
          logger.info({ productId, newPrice, flagged, CorrelationId: correlationId }, 'price change applied to carts');
        }
        await inbox.record(envelope.eventId, envelope.eventType);
        return 'handled';
      } catch (err) {
        const decision = retry.onFailure(originalTopic, retries, maxRetries, now());
        logger.warn({ err, target: decision.topic, attempt: decision.attempt }, 'price change failed');
        await forward(decision.topic, message, { originalTopic, retries: decision.attempt, error: err.message, notBeforeMs: decision.notBeforeMs });
        return decision.topic.endsWith(retry.DLQ_SUFFIX) ? 'dead-lettered' : 'retrying';
      }
    });
  };
}

async function startPriceConsumer({ brokers, groupId, maxRetries, store, inbox }) {
  const kafka = new Kafka({ clientId: 'cart', brokers, logLevel: logLevel.WARN, retry: { retries: 10 } });
  const consumer = kafka.consumer({ groupId });
  const producer = kafka.producer({ idempotent: true });
  const state = { connected: false };

  consumer.on(consumer.events.CONNECT, () => { state.connected = true; });
  consumer.on(consumer.events.DISCONNECT, () => { state.connected = false; });
  consumer.on(consumer.events.CRASH, () => { state.connected = false; });

  await producer.connect();
  await consumer.connect();
  await consumer.subscribe({ topics: [PRICE_CHANGED, PRICE_CHANGED + retry.RETRY_SUFFIX], fromBeginning: false });

  const forward = (topic, message, { originalTopic, retries, error, notBeforeMs }) => {
    const headers = { ...message.headers };
    for (const k of ['x-original-topic', 'x-retry-count', 'x-error', 'x-consumer-group', 'x-not-before']) delete headers[k];
    Object.assign(headers, {
      'x-original-topic': originalTopic,
      'x-consumer-group': groupId,
      'x-retry-count': String(retries),
      'x-error': String(error).slice(0, 500),
      ...(notBeforeMs ? { 'x-not-before': String(notBeforeMs) } : {}),
    });
    return producer.send({ topic, messages: [{ key: message.key, value: message.value, headers }] });
  };

  const handle = createMessageHandler({ groupId, maxRetries, store, inbox, forward });
  await consumer.run({
    autoCommit: false,
    eachMessage: async ({ topic, partition, message }) => {
      await handle(topic, message);
      await consumer.commitOffsets([{ topic, partition, offset: (BigInt(message.offset) + 1n).toString() }]);
    },
  });

  return {
    isConnected: () => state.connected,
    stop: async () => {
      await consumer.disconnect();
      await producer.disconnect();
    },
  };
}

module.exports = { startPriceConsumer, createMessageHandler, parseEnvelope, PRICE_CHANGED };
