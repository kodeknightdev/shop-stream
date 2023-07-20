import { Inject, Injectable, OnApplicationShutdown } from '@nestjs/common';
import Redis from 'ioredis';
import { APP_CONFIG, AppConfig } from '../config';

export const INBOX_REDIS = Symbol('INBOX_REDIS');

export const inboxRedisProvider = {
  provide: INBOX_REDIS,
  inject: [APP_CONFIG],
  useFactory: (config: AppConfig) => new Redis(config.redisUrl, { maxRetriesPerRequest: 2, lazyConnect: false }),
};

const TTL_SECONDS = 7 * 24 * 3600;

// processed event ids in redis, so a restart doesn't resend mail
@Injectable()
export class InboxService implements OnApplicationShutdown {
  constructor(@Inject(INBOX_REDIS) private readonly redis: Redis) {}

  private key(eventId: string) {
    return `email:processed_events:${eventId}`;
  }

  async seen(eventId: string): Promise<boolean> {
    return (await this.redis.exists(this.key(eventId))) === 1;
  }

  async record(eventId: string, eventType: string): Promise<void> {
    await this.redis.set(this.key(eventId), eventType || '1', 'EX', TTL_SECONDS);
  }

  async ping(): Promise<boolean> {
    try {
      return (await this.redis.ping()) === 'PONG';
    } catch {
      return false;
    }
  }

  onApplicationShutdown() {
    this.redis.disconnect();
  }
}
