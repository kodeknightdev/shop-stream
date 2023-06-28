'use strict';

const jwt = require('jsonwebtoken');
const RedisMock = require('ioredis-mock');
const { createApp } = require('../src/app');
const { createCartStore } = require('../src/cartStore');

const jwtConfig = { signingKey: 'test-signing-key-for-cart-tests-0000000000', issuer: 'shop-stream-identity', audience: 'shop-stream' };

const products = {
  'a1b2c3d4-0000-4000-8000-000000000001': { id: 'a1b2c3d4-0000-4000-8000-000000000001', sku: 'ELE-1001', name: 'Aurora Headphones', price: 129.99, imageUrl: 'https://img/1', isActive: true },
  'a1b2c3d4-0000-4000-8000-000000000002': { id: 'a1b2c3d4-0000-4000-8000-000000000002', sku: 'BOO-1002', name: 'Night Garden', price: 16.99, imageUrl: 'https://img/2', isActive: true },
  'a1b2c3d4-0000-4000-8000-000000000003': { id: 'a1b2c3d4-0000-4000-8000-000000000003', sku: 'TOY-1003', name: 'Retired Toy', price: 5, imageUrl: 'https://img/3', isActive: false },
};

function fakeCatalog(overrides = {}) {
  const data = JSON.parse(JSON.stringify(products));
  return {
    data,
    getProduct: jest.fn(async (id) => overrides.fail ? Promise.reject(new Error('down')) : data[id] || null),
  };
}

function token(sub = 'user-1', opts = {}) {
  return jwt.sign({ sub, email: `${sub}@example.com`, role: 'Customer' }, jwtConfig.signingKey, {
    issuer: jwtConfig.issuer, audience: jwtConfig.audience, expiresIn: opts.expiresIn || '10m',
  });
}

function build(catalogOverrides) {
  const redis = new RedisMock();
  const store = createCartStore(redis, 3600);
  const catalog = fakeCatalog(catalogOverrides);
  const app = createApp({ store, catalog, redis, jwtConfig });
  return { app, store, catalog, redis };
}

module.exports = { build, token, products, jwtConfig };
