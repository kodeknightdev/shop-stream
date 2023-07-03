'use strict';

const Redis = require('ioredis');
const config = require('./config');
const logger = require('./logger');
const { createApp } = require('./app');
const { createCartStore } = require('./cartStore');
const { createCatalogClient } = require('./catalogClient');
const { startPriceConsumer } = require('./kafka');
const { createInbox } = require('./inbox');

async function main() {
  if (!config.jwt.signingKey) throw new Error('JWT_SIGNING_KEY must be set');

  const redis = new Redis(config.redisUrl, { maxRetriesPerRequest: 3, enableReadyCheck: true });
  const store = createCartStore(redis, config.cartTtlSeconds);
  const catalog = createCatalogClient(config.catalogUrl);

  let kafka = null;
  if (config.kafka.enabled) {
    startPriceConsumer({ ...config.kafka, store, inbox: createInbox(redis) })
      .then((handle) => { kafka = handle; logger.info('price consumer started'); })
      .catch((err) => logger.error({ err }, 'price consumer failed to start'));
  }

  const app = createApp({
    store,
    catalog,
    redis,
    jwtConfig: config.jwt,
    kafkaStatus: () => !config.kafka.enabled || Boolean(kafka?.isConnected()),
  });

  const server = app.listen(config.port, () => logger.info(`cart listening on ${config.port}`));

  const shutdown = async () => {
    logger.info('shutting down');
    server.close();
    await kafka?.stop().catch(() => {});
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
