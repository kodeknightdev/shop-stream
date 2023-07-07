'use strict';

const request = require('supertest');
const { build, token } = require('./helpers');

const P1 = 'a1b2c3d4-0000-4000-8000-000000000001';
const P2 = 'a1b2c3d4-0000-4000-8000-000000000002';
const P3 = 'a1b2c3d4-0000-4000-8000-000000000003';

describe('cart API', () => {
  let ctx;
  let auth;

  beforeEach(async () => {
    ctx = build();
    await ctx.redis.flushall();
    auth = { Authorization: `Bearer ${token()}` };
  });

  test('requires a valid token', async () => {
    const res = await request(ctx.app).get('/api/cart');
    expect(res.status).toBe(401);
    expect(res.headers['content-type']).toMatch(/application\/problem\+json/);

    const expired = await request(ctx.app).get('/api/cart').set('Authorization', `Bearer ${token('u', { expiresIn: '-2m' })}`);
    expect(expired.status).toBe(401);
  });

  test('starts empty', async () => {
    const res = await request(ctx.app).get('/api/cart').set(auth);
    expect(res.status).toBe(200);
    expect(res.body).toMatchObject({ userId: 'user-1', items: [], itemCount: 0, subtotal: 0 });
  });

  test('adds items using catalog prices and merges quantities', async () => {
    await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: P1, quantity: 1 }).expect(200);
    await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: P2, quantity: 2 }).expect(200);
    const res = await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: P1, quantity: 2 }).expect(200);

    expect(res.body.items).toHaveLength(2);
    expect(res.body.items.find((i) => i.productId === P1).quantity).toBe(3);
    expect(res.body.subtotal).toBe(Math.round((129.99 * 3 + 16.99 * 2) * 100) / 100);
    expect(res.body.itemCount).toBe(5);
  });

  test('caps quantity at 20', async () => {
    await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: P1, quantity: 15 });
    const res = await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: P1, quantity: 10 });
    expect(res.body.items[0].quantity).toBe(20);
  });

  test('validates input', async () => {
    const res = await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: 'nope', quantity: 0 });
    expect(res.status).toBe(400);
    expect(Object.keys(res.body.errors)).toEqual(expect.arrayContaining(['productId', 'quantity']));
  });

  test('rejects inactive or unknown products', async () => {
    await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: P3 }).expect(404);
    await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: 'a1b2c3d4-0000-4000-8000-0000000000ff' }).expect(404);
  });

  test('returns 503 when catalog is down', async () => {
    ctx = build({ fail: true });
    const res = await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: P1 });
    expect(res.status).toBe(503);
  });

  test('updates, removes and clears', async () => {
    await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: P1 });
    await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: P2 });

    const updated = await request(ctx.app).put(`/api/cart/items/${P1}`).set(auth).send({ quantity: 4 }).expect(200);
    expect(updated.body.items.find((i) => i.productId === P1).quantity).toBe(4);

    const removed = await request(ctx.app).delete(`/api/cart/items/${P2}`).set(auth).expect(200);
    expect(removed.body.items.map((i) => i.productId)).toEqual([P1]);

    await request(ctx.app).put(`/api/cart/items/${P2}`).set(auth).send({ quantity: 1 }).expect(404);
    await request(ctx.app).delete('/api/cart').set(auth).expect(204);
    const empty = await request(ctx.app).get('/api/cart').set(auth);
    expect(empty.body.items).toEqual([]);
  });

  test('carts are isolated per user and expire', async () => {
    await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: P1 });
    const other = await request(ctx.app).get('/api/cart').set('Authorization', `Bearer ${token('user-2')}`);
    expect(other.body.items).toEqual([]);
    expect(await ctx.redis.ttl('cart:user-1')).toBeGreaterThan(0);
  });

  test('validate applies the latest catalog prices', async () => {
    await request(ctx.app).post('/api/cart/items').set(auth).send({ productId: P1 });
    ctx.catalog.data[P1].price = 99.0;
    const res = await request(ctx.app).post('/api/cart/validate').set(auth).expect(200);
    expect(res.body.changes).toEqual([{ productId: P1, name: 'Aurora Headphones', oldPrice: 129.99, newPrice: 99 }]);
    expect(res.body.cart.subtotal).toBe(99);
  });

  test('health endpoints', async () => {
    await request(ctx.app).get('/health').expect(200, { status: 'Healthy' });
    const ready = await request(ctx.app).get('/health/ready');
    expect(ready.body.checks.redis).toBe('Healthy');
  });

  test('echoes the correlation id', async () => {
    const res = await request(ctx.app).get('/health').set('X-Correlation-ID', 'abc123');
    expect(res.headers['x-correlation-id']).toBe('abc123');
  });
});
