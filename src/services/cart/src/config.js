'use strict';

function bool(value, fallback) {
  if (value === undefined || value === '') return fallback;
  return ['1', 'true', 'yes'].includes(String(value).toLowerCase());
}

module.exports = {
  port: Number(process.env.PORT || 3001),
  redisUrl: process.env.REDIS_URL || 'redis://localhost:6379/1',
  cartTtlSeconds: Number(process.env.CART_TTL_SECONDS || 7 * 24 * 3600),
  catalogUrl: process.env.CATALOG_URL || 'http://localhost:5002',
  jwt: {
    signingKey: process.env.JWT_SIGNING_KEY || '',
    issuer: process.env.JWT_ISSUER || 'shop-stream-identity',
    audience: process.env.JWT_AUDIENCE || 'shop-stream',
  },
  kafka: {
    enabled: bool(process.env.KAFKA_ENABLED, true),
    brokers: (process.env.KAFKA_BOOTSTRAP_SERVERS || 'localhost:9094').split(','),
    groupId: process.env.KAFKA_GROUP_ID || 'cart',
    maxRetries: Number(process.env.KAFKA_MAX_RETRIES || 3),
  },
  logLevel: process.env.LOG_LEVEL || 'info',
};
