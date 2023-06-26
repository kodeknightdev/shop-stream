'use strict';

const {
  bulkhead, circuitBreaker, ConsecutiveBreaker, ExponentialBackoff, handleWhen, retry, timeout, TimeoutStrategy, wrap,
  isBrokenCircuitError, isBulkheadRejectedError, isTaskCancelledError,
} = require('cockatiel');
const logger = require('./logger');
const { currentCorrelationId } = require('./correlation');

class CatalogError extends Error {
  constructor(message, { transient }) {
    super(message);
    this.transient = transient;
  }
}

const isTransient = (err) => !(err instanceof CatalogError) || err.transient;

// bulkhead -> retry -> breaker -> timeout, same as the Polly setup in orders.
// only network errors, 5xx and timeouts count.
function createCatalogPolicy({ timeoutMs = 2000, retries = 2, failuresBeforeBreaking = 5, breakMs = 30_000, maxParallel = 10, maxQueue = 20 } = {}) {
  const transient = handleWhen(isTransient);
  const breaker = circuitBreaker(transient, { halfOpenAfter: breakMs, breaker: new ConsecutiveBreaker(failuresBeforeBreaking) });
  breaker.onBreak(() => logger.warn('circuit to catalog opened'));
  breaker.onReset(() => logger.info('circuit to catalog closed'));

  return wrap(
    bulkhead(maxParallel, maxQueue),
    retry(transient, { maxAttempts: retries, backoff: new ExponentialBackoff({ initialDelay: 100, maxDelay: 2000 }) }),
    breaker,
    timeout(timeoutMs, TimeoutStrategy.Aggressive),
  );
}

const isDependencyFailure = (err) =>
  isBrokenCircuitError(err) || isBulkheadRejectedError(err) || isTaskCancelledError(err) || isTransient(err);

function createCatalogClient(baseUrl, { fetchImpl = fetch, policy = createCatalogPolicy() } = {}) {
  async function getProduct(productId) {
    return policy.execute(async ({ signal }) => {
      const res = await fetchImpl(`${baseUrl}/api/products/${productId}`, {
        headers: { accept: 'application/json', 'x-correlation-id': currentCorrelationId() || '' },
        signal,
      });
      if (res.status === 404) return null;
      if (!res.ok) throw new CatalogError(`catalog responded ${res.status}`, { transient: res.status >= 500 });
      const p = await res.json();
      return { id: p.id, sku: p.sku, name: p.name, price: Number(p.price), imageUrl: p.imageUrl, isActive: p.isActive };
    });
  }

  return { getProduct };
}

module.exports = { createCatalogClient, createCatalogPolicy, isDependencyFailure, CatalogError };
