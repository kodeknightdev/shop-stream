'use strict';

const { randomUUID } = require('crypto');
const { AsyncLocalStorage } = require('async_hooks');

const HEADER = 'x-correlation-id';
const storage = new AsyncLocalStorage();

function correlationMiddleware(req, res, next) {
  const id = req.get(HEADER) || randomUUID().replace(/-/g, '');
  req.correlationId = id;
  res.set('X-Correlation-ID', id);
  storage.run(id, next);
}

const currentCorrelationId = () => storage.getStore();
const runWithCorrelationId = (id, fn) => storage.run(id, fn);

module.exports = { HEADER, correlationMiddleware, currentCorrelationId, runWithCorrelationId };
