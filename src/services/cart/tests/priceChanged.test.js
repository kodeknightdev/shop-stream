'use strict';

const RedisMock = require('ioredis-mock');
const { createCartStore } = require('../src/cartStore');
const { parseEnvelope, createMessageHandler, PRICE_CHANGED } = require('../src/kafka');
const { createInbox } = require('../src/inbox');
const retry = require('../src/retry');
const { products } = require('./helpers');

const P1 = 'a1b2c3d4-0000-4000-8000-000000000001';

describe('price change handling', () => {
  test('flags lines in every cart holding the product', async () => {
    const redis = new RedisMock();
    await redis.flushall();
    const store = createCartStore(redis, 3600);
    await store.addItem('u1', products[P1], 1, 20);
    await store.addItem('u2', products[P1], 2, 20);

    const flagged = await store.flagPriceChange(P1, 109.99);

    expect(flagged).toBe(2);
    const cart = await store.get('u1');
    expect(cart.hasStalePrices).toBe(true);
    expect(cart.items[0]).toMatchObject({ priceStale: true, latestPrice: 109.99, unitPrice: 129.99 });
  });

  test('re-adding a flagged item refreshes the price', async () => {
    const redis = new RedisMock();
    await redis.flushall();
    const store = createCartStore(redis, 3600);
    await store.addItem('u1', products[P1], 1, 20);
    await store.flagPriceChange(P1, 109.99);
    const cart = await store.addItem('u1', { ...products[P1], price: 109.99 }, 1, 20);
    expect(cart.items[0]).toMatchObject({ priceStale: false, unitPrice: 109.99, quantity: 2 });
  });

  test('parses envelopes and rejects junk', () => {
    const env = parseEnvelope(Buffer.from(JSON.stringify({ eventId: '1', payload: { productId: P1 } })));
    expect(env.payload.productId).toBe(P1);
    expect(() => parseEnvelope(Buffer.from('nope'))).toThrow();
    expect(() => parseEnvelope(Buffer.from('{}'))).toThrow();
  });
});

describe('price-changed consumer', () => {
  const envelope = (eventId, payload) => Buffer.from(JSON.stringify({ eventId, eventType: 'ProductPriceChanged', payload }));

  async function setup(storeOverride) {
    const redis = new RedisMock();
    await redis.flushall();
    const store = storeOverride || createCartStore(redis, 3600);
    const forwarded = [];
    const handle = createMessageHandler({
      groupId: 'cart', maxRetries: 2, store, inbox: createInbox(redis),
      forward: async (topic, message, meta) => forwarded.push({ topic, meta }),
      now: () => 1_000, wait: async () => {},
    });
    return { redis, store, handle, forwarded };
  }

  test('a redelivered event is skipped by the inbox', async () => {
    const { store, handle, redis } = await setup();
    await store.addItem('u1', products[P1], 1, 20);
    const message = { value: envelope('evt-1', { productId: P1, newPrice: 99 }), headers: {} };

    expect(await handle(PRICE_CHANGED, message)).toBe('handled');
    expect(await handle(PRICE_CHANGED, message)).toBe('duplicate');
    expect(await redis.exists('cart:processed_events:evt-1')).toBe(1);
  });

  test('failures go to <topic>.retry with backoff, then <topic>.dlq', async () => {
    const failing = { flagPriceChange: jest.fn().mockRejectedValue(new Error('redis down')) };
    const { handle, forwarded } = await setup(failing);

    await handle(PRICE_CHANGED, { value: envelope('evt-2', { productId: P1, newPrice: 1 }), headers: {} });
    await handle(PRICE_CHANGED + '.retry', {
      value: envelope('evt-2', { productId: P1, newPrice: 1 }),
      headers: { 'x-consumer-group': 'cart', 'x-retry-count': '2', 'x-original-topic': PRICE_CHANGED },
    });

    expect(forwarded[0]).toMatchObject({ topic: 'catalog.product-price-changed.retry', meta: { retries: 1, notBeforeMs: 1_000 + retry.backoffMs(1) } });
    expect(forwarded[1]).toMatchObject({ topic: 'catalog.product-price-changed.dlq', meta: { retries: 3, notBeforeMs: null } });
  });

  test('ignores retry copies that belong to another consumer group', async () => {
    const { handle } = await setup();
    const message = { value: envelope('evt-3', { productId: P1, newPrice: 1 }), headers: { 'x-consumer-group': 'email' } };
    expect(await handle(PRICE_CHANGED + '.retry', message)).toBe('skipped');
  });

  test('poison messages go straight to the dlq', async () => {
    const { handle, forwarded } = await setup();
    expect(await handle(PRICE_CHANGED, { value: Buffer.from('nope'), headers: {} })).toBe('dead-lettered');
    expect(forwarded[0].topic).toBe('catalog.product-price-changed.dlq');
  });
});
