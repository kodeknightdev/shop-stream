'use strict';

const { isBrokenCircuitError } = require('cockatiel');
const { createCatalogClient, createCatalogPolicy, isDependencyFailure } = require('../src/catalogClient');

const ok = (body) => ({ status: 200, ok: true, json: async () => body });
const status = (code) => ({ status: code, ok: code < 400, json: async () => ({}) });
const product = { id: 'p1', sku: 'S', name: 'Lamp', price: '10.5', imageUrl: 'x', isActive: true };

const fastPolicy = (overrides = {}) =>
  createCatalogPolicy({ timeoutMs: 200, retries: 2, failuresBeforeBreaking: 3, breakMs: 60_000, ...overrides });

describe('cart -> catalog resilience', () => {
  test('retries transient 5xx responses and then succeeds', async () => {
    const fetchImpl = jest.fn()
      .mockResolvedValueOnce(status(503))
      .mockResolvedValueOnce(status(502))
      .mockResolvedValueOnce(ok(product));
    const client = createCatalogClient('http://catalog', { fetchImpl, policy: fastPolicy() });

    await expect(client.getProduct('p1')).resolves.toMatchObject({ id: 'p1', price: 10.5 });
    expect(fetchImpl).toHaveBeenCalledTimes(3);
  });

  test('does not retry 4xx and returns null for 404', async () => {
    const fetchImpl = jest.fn().mockResolvedValueOnce(status(404)).mockResolvedValueOnce(status(400));
    const client = createCatalogClient('http://catalog', { fetchImpl, policy: fastPolicy() });

    await expect(client.getProduct('missing')).resolves.toBeNull();
    await expect(client.getProduct('bad')).rejects.toThrow('400');
    expect(fetchImpl).toHaveBeenCalledTimes(2);
  });

  test('opens the circuit after consecutive failures and stops calling catalog', async () => {
    const fetchImpl = jest.fn().mockRejectedValue(new Error('ECONNREFUSED'));
    const client = createCatalogClient('http://catalog', { fetchImpl, policy: fastPolicy({ retries: 0 }) });

    for (let i = 0; i < 3; i += 1) await expect(client.getProduct('p1')).rejects.toThrow('ECONNREFUSED');
    const err = await client.getProduct('p1').catch((e) => e);

    expect(isBrokenCircuitError(err)).toBe(true);
    expect(isDependencyFailure(err)).toBe(true);
    expect(fetchImpl).toHaveBeenCalledTimes(3);
  });

  test('times out slow calls', async () => {
    const fetchImpl = jest.fn((url, { signal }) => new Promise((resolve, reject) => {
      signal.addEventListener('abort', () => reject(new Error('aborted')));
    }));
    const client = createCatalogClient('http://catalog', { fetchImpl, policy: fastPolicy({ retries: 0, timeoutMs: 50 }) });

    const err = await client.getProduct('p1').catch((e) => e);
    expect(isDependencyFailure(err)).toBe(true);
  });

  test('bulkhead rejects calls beyond the limit', async () => {
    let release;
    const fetchImpl = jest.fn(() => new Promise((r) => { release = () => r(ok(product)); }));
    const client = createCatalogClient('http://catalog', { fetchImpl, policy: fastPolicy({ maxParallel: 1, maxQueue: 0, timeoutMs: 1000 }) });

    const first = client.getProduct('p1');
    await expect(client.getProduct('p2')).rejects.toThrow(/Bulkhead/i);
    release();
    await expect(first).resolves.toMatchObject({ id: 'p1' });
  });
});
