import { Inject, Injectable, Logger, OnApplicationShutdown, OnModuleInit } from '@nestjs/common';
import { Consumer, Kafka, KafkaMessage, logLevel, Producer } from 'kafkajs';
import { APP_CONFIG, AppConfig } from '../config';
import { runWithCorrelationId } from '../common/correlation';
import { Envelope, NotificationsService, TOPICS } from '../notifications/notifications.service';
import { DLQ_SUFFIX, isRetryTopic, MAX_BACKOFF_MS, onFailure, RETRY_SUFFIX, sourceTopic } from './retry';

const header = (message: KafkaMessage, key: string) => message.headers?.[key]?.toString();

export function parseEnvelope(value: Buffer | null): Envelope {
  const data = JSON.parse(value?.toString() ?? '');
  if (!data || typeof data !== 'object' || !data.eventId || !data.payload) {
    throw new Error('Not a shop-stream envelope');
  }
  return data as Envelope;
}

@Injectable()
export class KafkaConsumerService implements OnModuleInit, OnApplicationShutdown {
  private readonly logger = new Logger(KafkaConsumerService.name);
  private consumer?: Consumer;
  private producer?: Producer;
  private connected = false;

  constructor(@Inject(APP_CONFIG) private readonly config: AppConfig, private readonly notifications: NotificationsService) {}

  get isConnected() {
    return !this.config.kafka.enabled || this.connected;
  }

  async onModuleInit() {
    if (!this.config.kafka.enabled) {
      this.logger.warn('Kafka disabled; no emails will be sent from events');
      return;
    }
    // don't block startup on kafka
    this.start().catch((err) => this.logger.error(`Kafka consumer failed to start: ${err.message}`, err.stack));
  }

  private async start() {
    const { brokers, groupId } = this.config.kafka;
    const kafka = new Kafka({ clientId: 'email', brokers, logLevel: logLevel.WARN, retry: { retries: 10 } });
    const topics = Object.values(TOPICS) as string[];
    const subscriptions = [...topics, ...topics.map((t) => t + RETRY_SUFFIX)];

    this.producer = kafka.producer();
    this.consumer = kafka.consumer({ groupId });
    this.consumer.on(this.consumer.events.CONNECT, () => (this.connected = true));
    this.consumer.on(this.consumer.events.DISCONNECT, () => (this.connected = false));
    this.consumer.on(this.consumer.events.CRASH, () => (this.connected = false));

    await this.producer.connect();
    await this.consumer.connect();
    await this.consumer.subscribe({ topics: subscriptions, fromBeginning: false });

    await this.consumer.run({
      autoCommit: false,
      eachMessage: async ({ topic, partition, message }) => {
        await this.handleMessage(topic, message);
        await this.consumer!.commitOffsets([{ topic, partition, offset: (BigInt(message.offset) + 1n).toString() }]);
      },
    });
    this.logger.log(`Subscribed to ${subscriptions.join(', ')}`);
  }

  async handleMessage(topic: string, message: KafkaMessage, now: () => number = Date.now): Promise<string> {
    const { groupId, maxRetries } = this.config.kafka;
    const fromRetry = isRetryTopic(topic);
    const owner = header(message, 'x-consumer-group');
    if (fromRetry && owner && owner !== groupId) return 'skipped';

    const originalTopic = header(message, 'x-original-topic') ?? sourceTopic(topic);
    const retries = Number(header(message, 'x-retry-count') ?? 0);
    const notBefore = Number(header(message, 'x-not-before') ?? 0);
    if (fromRetry && notBefore > now()) await new Promise((r) => setTimeout(r, Math.min(notBefore - now(), MAX_BACKOFF_MS)));

    let envelope: Envelope;
    try {
      envelope = parseEnvelope(message.value);
    } catch (err) {
      this.logger.error(`Poison message on ${topic}; parking on ${originalTopic + DLQ_SUFFIX}`);
      await this.forward(originalTopic + DLQ_SUFFIX, message, originalTopic, retries, (err as Error).message, null);
      return 'dead-lettered';
    }

    const correlationId = header(message, 'correlation-id') ?? envelope.correlationId ?? envelope.eventId;
    return runWithCorrelationId(correlationId, async () => {
      try {
        return (await this.notifications.handle(originalTopic, envelope)) ? 'sent' : 'ignored';
      } catch (err) {
        const decision = onFailure(originalTopic, retries, maxRetries, now());
        this.logger.warn(`Email for ${envelope.eventType} ${envelope.eventId} failed (attempt ${decision.attempt}): ${(err as Error).message}`);
        await this.forward(decision.topic, message, originalTopic, decision.attempt, (err as Error).message, decision.notBeforeMs);
        return decision.notBeforeMs === null ? 'dead-lettered' : 'retrying';
      }
    });
  }

  private forward(topic: string, message: KafkaMessage, originalTopic: string, retries: number, error: string, notBeforeMs: number | null) {
    const headers: Record<string, string | Buffer> = {};
    for (const [k, v] of Object.entries(message.headers ?? {})) {
      if (v !== undefined && !['x-original-topic', 'x-retry-count', 'x-error', 'x-consumer-group', 'x-not-before'].includes(k)) {
        headers[k] = v as Buffer;
      }
    }
    Object.assign(headers, {
      'x-original-topic': originalTopic,
      'x-consumer-group': this.config.kafka.groupId,
      'x-retry-count': String(retries),
      'x-error': error.slice(0, 500),
      ...(notBeforeMs ? { 'x-not-before': String(notBeforeMs) } : {}),
    });
    return this.producer!.send({ topic, messages: [{ key: message.key, value: message.value, headers }] });
  }

  async onApplicationShutdown() {
    await this.consumer?.disconnect().catch(() => undefined);
    await this.producer?.disconnect().catch(() => undefined);
  }
}
