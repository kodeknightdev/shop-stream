'use strict';

const item = {
  type: 'object',
  properties: {
    productId: { type: 'string', format: 'uuid' },
    sku: { type: 'string' },
    name: { type: 'string' },
    imageUrl: { type: 'string' },
    unitPrice: { type: 'number' },
    quantity: { type: 'integer' },
    priceStale: { type: 'boolean' },
    latestPrice: { type: 'number', nullable: true },
  },
};

const cart = {
  type: 'object',
  properties: {
    userId: { type: 'string' },
    items: { type: 'array', items: item },
    itemCount: { type: 'integer' },
    subtotal: { type: 'number' },
    hasStalePrices: { type: 'boolean' },
    updatedAt: { type: 'string', format: 'date-time', nullable: true },
  },
};

const ok = (schema) => ({ 200: { description: 'OK', content: { 'application/json': { schema } } } });
const problem = { description: 'Problem details', content: { 'application/problem+json': { schema: { type: 'object' } } } };

module.exports = {
  openapi: '3.0.3',
  info: { title: 'shop-stream cart', version: '1.0.0', description: 'Per-user carts persisted in Redis with a sliding TTL.' },
  components: {
    securitySchemes: { bearer: { type: 'http', scheme: 'bearer', bearerFormat: 'JWT' } },
    schemas: { Cart: cart, CartItem: item },
  },
  security: [{ bearer: [] }],
  paths: {
    '/api/cart': {
      get: { tags: ['cart'], summary: 'Get the current user cart', responses: ok(cart) },
      delete: { tags: ['cart'], summary: 'Empty the cart', responses: { 204: { description: 'Cleared' } } },
    },
    '/api/cart/items': {
      post: {
        tags: ['cart'],
        summary: 'Add a product (quantities are merged)',
        requestBody: {
          required: true,
          content: {
            'application/json': {
              schema: { type: 'object', required: ['productId'], properties: { productId: { type: 'string', format: 'uuid' }, quantity: { type: 'integer', minimum: 1, maximum: 20 } } },
            },
          },
        },
        responses: { ...ok(cart), 400: problem, 404: problem, 503: problem },
      },
    },
    '/api/cart/items/{productId}': {
      parameters: [{ name: 'productId', in: 'path', required: true, schema: { type: 'string', format: 'uuid' } }],
      put: {
        tags: ['cart'],
        summary: 'Set quantity (0 removes the line)',
        requestBody: { required: true, content: { 'application/json': { schema: { type: 'object', properties: { quantity: { type: 'integer', minimum: 0, maximum: 20 } } } } } },
        responses: { ...ok(cart), 404: problem },
      },
      delete: { tags: ['cart'], summary: 'Remove a line', responses: { ...ok(cart), 404: problem } },
    },
    '/api/cart/validate': {
      post: { tags: ['cart'], summary: 'Re-check every line against the catalog and apply current prices', responses: ok({ type: 'object' }) },
    },
  },
};
