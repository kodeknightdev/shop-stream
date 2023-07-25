import { KafkaMessage } from 'kafkajs';
import { config } from '../src/config';
import { KafkaConsumerService } from '../src/kafka/kafka-consumer.service';
import { backoffMs } from '../src/kafka/retry';
import { NotificationsService, TOPICS } from '../src/notifications/notifications.service';

const message = (body: unknown, headers: Record<string, string> = {}): KafkaMessage =>
  ({ key: Buffer.from('k'), value: Buffer.from(typeof body === 'string' ? body : JSON.stringify(body)), headers, offset: '0', timestamp: '0', attributes: 0 }) as KafkaMessage;

describe('KafkaConsumerService.handleMessage', () => {
  const send = jest.fn();
  const handle = jest.fn();
  let service: KafkaConsumerService;

  beforeEach(() => {
    send.mockReset().mockResolvedValue(undefined);
    handle.mockReset();
    service = new KafkaConsumerService({ ...config, kafka: { ...config.kafka, groupId: 'email', maxRetries: 2 } },
      { handle } as unknown as NotificationsService);
    (service as unknown as { producer: { send: jest.Mock } }).producer = { send };
  });

  const event = { eventId: 'e1', eventType: 'OrderConfirmed', schemaVersion: 1, occurredAt: '', payload: { orderId: 'o1' } };

  it('sends and reports success', async () => {
    handle.mockResolvedValue(true);
    await expect(service.handleMessage(TOPICS.orderConfirmed, message(event))).resolves.toBe('sent');
  });

  it('routes a failure to <topic>.retry with backoff', async () => {
    handle.mockRejectedValue(new Error('SMTP down'));
    await expect(service.handleMessage(TOPICS.orderConfirmed, message(event), () => 5_000)).resolves.toBe('retrying');

    const sent = send.mock.calls[0][0];
    expect(sent.topic).toBe('orders.order-confirmed.retry');
    expect(sent.messages[0].headers).toMatchObject({
      'x-consumer-group': 'email', 'x-retry-count': '1', 'x-original-topic': 'orders.order-confirmed', 'x-not-before': String(5_000 + backoffMs(1)),
    });
  });

  it('dead-letters after the last retry', async () => {
    handle.mockRejectedValue(new Error('SMTP down'));
    const retried = message(event, { 'x-consumer-group': 'email', 'x-retry-count': '2', 'x-original-topic': TOPICS.orderConfirmed });
    await expect(service.handleMessage(TOPICS.orderConfirmed + '.retry', retried)).resolves.toBe('dead-lettered');
    expect(send.mock.calls[0][0].topic).toBe('orders.order-confirmed.dlq');
  });

  it('ignores retry copies addressed to another group and parks poison messages', async () => {
    await expect(service.handleMessage('orders.order-placed.retry', message(event, { 'x-consumer-group': 'inventory' }))).resolves.toBe('skipped');
    await expect(service.handleMessage(TOPICS.orderPlaced, message('not json'))).resolves.toBe('dead-lettered');
    expect(send.mock.calls[0][0].topic).toBe('orders.order-placed.dlq');
    expect(handle).not.toHaveBeenCalled();
  });
});
