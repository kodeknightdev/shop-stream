'use strict';

const Redis = require('ioredis');
const config = require('./config');
const logger = require('./logger');
const { createApp } = require('./app');
const { createCartStore } = require('./cartStore');
const { createCatalogClient } = require('./catalogClient');
const { createInbox } = require('./inbox');

async function main() {
  if (!config.jwt.signingKey) throw new Error('JWT_SIGNING_KEY must be set');

  const redis = new Redis(config.redisUrl, { maxRetriesPerRequest: 3, enableReadyCheck: true });
  const store = createCartStore(redis, config.cartTtlSeconds);
  const catalog = createCatalogClient(config.catalogUrl);

  const app = createApp({
    store,
    catalog,
    redis,
    jwtConfig: config.jwt,
  });

  const server = app.listen(config.port, () => logger.info(`cart listening on ${config.port}`));

  const shutdown = async () => {
    logger.info('shutting down');
    server.close();
    redis.disconnect();
    process.exit(0);
  };
  process.on('SIGTERM', shutdown);
  process.on('SIGINT', shutdown);
}

main().catch((err) => {
  logger.fatal({ err }, 'cart failed to start');
  process.exit(1);
});
