'use strict';

const express = require('express');
const pinoHttp = require('pino-http');
const swaggerUi = require('swagger-ui-express');

const baseLogger = require('./logger');
const openapi = require('./openapi');
const { correlationMiddleware } = require('./correlation');
const { requireUser } = require('./auth');
const { schemas, validate, MAX_QUANTITY } = require('./validation');
const { errorHandler, notFound, unavailable, sendProblem } = require('./errors');

const wrap = (fn) => (req, res, next) => Promise.resolve(fn(req, res, next)).catch(next);

function createApp({ store, catalog, redis, jwtConfig, kafkaStatus = () => true, logger = baseLogger }) {
  const app = express();
  app.disable('x-powered-by');
  app.use(express.json({ limit: '32kb' }));
  app.use(correlationMiddleware);
  app.use(pinoHttp({ logger, customProps: (req) => ({ CorrelationId: req.correlationId }), autoLogging: { ignore: (req) => req.url.startsWith('/health') } }));

  app.get('/health', (req, res) => res.json({ status: 'Healthy' }));
  app.get('/health/ready', wrap(async (req, res) => {
    const checks = {};
    try {
      checks.redis = (await redis.ping()) === 'PONG' ? 'Healthy' : 'Unhealthy';
    } catch (err) {
      checks.redis = 'Unhealthy';
    }
    checks.kafka = kafkaStatus() ? 'Healthy' : 'Unhealthy';
    const healthy = Object.values(checks).every((v) => v === 'Healthy');
    res.status(healthy ? 200 : 503).json({ status: healthy ? 'Healthy' : 'Unhealthy', checks });
  }));

  app.get('/api-docs/openapi.json', (req, res) => res.json(openapi));
  app.use('/api-docs', swaggerUi.serve, swaggerUi.setup(openapi));

  const cart = express.Router();
  cart.use(requireUser(jwtConfig));

  cart.get('/', wrap(async (req, res) => res.json(await store.get(req.user.id))));

  cart.delete('/', wrap(async (req, res) => {
    await store.clear(req.user.id);
    res.status(204).end();
  }));

  cart.post('/items', validate(schemas.addItem), wrap(async (req, res) => {
    const productId = req.body.productId.toLowerCase();
    let product;
    try {
      product = await catalog.getProduct(productId);
    } catch (err) {
      req.log.warn({ err }, 'catalog lookup failed');
      throw unavailable('The catalog could not be reached. Please try again.');
    }
    if (!product || !product.isActive) throw notFound(`Product ${productId} is not available.`);
    res.json(await store.addItem(req.user.id, product, req.body.quantity, MAX_QUANTITY));
  }));

  cart.put('/items/:productId', validate(schemas.productParam, 'params'), validate(schemas.updateItem), wrap(async (req, res) => {
    const result = await store.setQuantity(req.user.id, req.params.productId.toLowerCase(), req.body.quantity);
    if (!result) throw notFound('That product is not in your cart.');
    res.json(result);
  }));

  cart.delete('/items/:productId', validate(schemas.productParam, 'params'), wrap(async (req, res) => {
    const result = await store.removeItem(req.user.id, req.params.productId.toLowerCase());
    if (!result) throw notFound('That product is not in your cart.');
    res.json(result);
  }));

  cart.post('/validate', wrap(async (req, res) => {
    try {
      res.json(await store.revalidate(req.user.id, catalog));
    } catch (err) {
      if (err.status) throw err;
      throw unavailable('Prices could not be checked right now.');
    }
  }));

  app.use('/api/cart', cart);
  app.use((req, res) => sendProblem(req, res, 404, 'Resource not found', `No route for ${req.method} ${req.path}`));
  app.use(errorHandler);
  return app;
}

module.exports = { createApp };
