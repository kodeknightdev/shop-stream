const bool = (value: string | undefined, fallback: boolean) =>
  value === undefined || value === '' ? fallback : ['1', 'true', 'yes'].includes(value.toLowerCase());

export const config = {
  port: Number(process.env.PORT ?? 3002),
  smtp: {
    host: process.env.SMTP_HOST ?? 'localhost',
    port: Number(process.env.SMTP_PORT ?? 1025),
    from: process.env.MAIL_FROM ?? 'shop-stream <orders@shopstream.local>',
  },
  shopUrl: process.env.SHOP_URL ?? 'http://localhost:3000',
  redisUrl: process.env.REDIS_URL ?? 'redis://localhost:6379/2',
  jwt: {
    signingKey: process.env.JWT_SIGNING_KEY ?? '',
    issuer: process.env.JWT_ISSUER ?? 'shop-stream-identity',
    audience: process.env.JWT_AUDIENCE ?? 'shop-stream',
  },
  kafka: {
    enabled: bool(process.env.KAFKA_ENABLED, true),
    brokers: (process.env.KAFKA_BOOTSTRAP_SERVERS ?? 'localhost:9094').split(','),
    groupId: process.env.KAFKA_GROUP_ID ?? 'email',
    maxRetries: Number(process.env.KAFKA_MAX_RETRIES ?? 3),
  },
};

export type AppConfig = typeof config;
export const APP_CONFIG = Symbol('APP_CONFIG');
