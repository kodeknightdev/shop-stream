import { join } from 'path';
import RedisMock from 'ioredis-mock';
import { INBOX_REDIS, InboxService } from '../src/inbox/inbox.service';
import { Test } from '@nestjs/testing';
import { APP_CONFIG, config } from '../src/config';
import { MAIL_TRANSPORT, MailerService } from '../src/mail/mailer.service';
import { TEMPLATES_DIR, TemplatesService } from '../src/mail/templates.service';
import { NotificationsService, TOPICS } from '../src/notifications/notifications.service';
import { parseEnvelope } from '../src/kafka/kafka-consumer.service';

const orderId = '3f2a8c1e-5b6d-4e7f-8a9b-0c1d2e3f4a5b';
const envelope = (payload: Record<string, unknown>, eventId = 'evt-1') => ({
  eventId, eventType: 'X', schemaVersion: 1, occurredAt: new Date().toISOString(), payload,
});

describe('NotificationsService', () => {
  let service: NotificationsService;
  let mailer: MailerService;
  let moduleTemplates: TemplatesService;
  const sendMail = jest.fn();
  let redis: InstanceType<typeof RedisMock>;

  beforeEach(async () => {
    redis = new RedisMock();
    await redis.flushall();
    sendMail.mockReset().mockResolvedValue({ messageId: '<m1@test>' });
    const moduleRef = await Test.createTestingModule({
      providers: [
        { provide: APP_CONFIG, useValue: { ...config, shopUrl: 'http://shop.test' } },
        { provide: MAIL_TRANSPORT, useValue: { sendMail, verify: jest.fn().mockResolvedValue(true) } },
        { provide: TEMPLATES_DIR, useValue: join(__dirname, '..', 'templates') },
        { provide: INBOX_REDIS, useValue: redis },
        InboxService,
        TemplatesService,
        MailerService,
        NotificationsService,
      ],
    }).compile();
    await moduleRef.init();
    service = moduleRef.get(NotificationsService);
    mailer = moduleRef.get(MailerService);
    moduleTemplates = moduleRef.get(TemplatesService);
  });

  it('sends an order placed email listing the items', async () => {
    const sent = await service.handle(TOPICS.orderPlaced, envelope({
      orderId, email: 'casey@example.com', currency: 'USD', total: 146.98,
      items: [{ name: 'Aurora Wireless Headphones', quantity: 1, unitPrice: 129.99 }, { name: 'Night Garden', quantity: 1, unitPrice: 16.99 }],
    }));

    expect(sent).toBe(true);
    const mail = sendMail.mock.calls[0][0];
    expect(mail.to).toBe('casey@example.com');
    expect(mail.subject).toBe('We received your order 3F2A8C1E');
    expect(mail.html).toContain('Aurora Wireless Headphones');
    expect(mail.html).toContain('$146.98');
    expect(mail.html).toContain(`http://shop.test/orders/${orderId}`);
  });

  it.each([
    [TOPICS.orderConfirmed, { total: 20, currency: 'USD' }, 'is confirmed'],
    [TOPICS.orderCancelled, { reason: 'Inventory: Insufficient stock' }, 'was cancelled'],
    [TOPICS.paymentFailed, { reason: 'Your card was declined.', amount: 20 }, 'Payment failed'],
  ])('renders %s', async (topic, extra, subjectPart) => {
    await service.handle(topic, envelope({ orderId, email: 'casey@example.com', ...extra }));
    expect(sendMail.mock.calls[0][0].subject).toContain(subjectPart);
  });

  it('does not send twice for the same event id, even after a restart', async () => {
    const e = envelope({ orderId, email: 'casey@example.com', total: 1 }, 'evt-dup');
    await service.handle(TOPICS.orderConfirmed, e);
    await service.handle(TOPICS.orderConfirmed, e);
    expect(sendMail).toHaveBeenCalledTimes(1);
    expect(await redis.exists('email:processed_events:evt-dup')).toBe(1);

    // new instance ~ restart, the inbox entry is still in redis
    const restarted = new NotificationsService(moduleTemplates, mailer, new InboxService(redis as never), { ...config, shopUrl: 'http://shop.test' });
    expect(await restarted.handle(TOPICS.orderConfirmed, e)).toBe(false);
  });

  it('ignores unknown topics and missing recipients', async () => {
    expect(await service.handle('something.else', envelope({ orderId }))).toBe(false);
    expect(await service.handle(TOPICS.orderConfirmed, envelope({ orderId }, 'evt-2'))).toBe(false);
    expect(sendMail).not.toHaveBeenCalled();
  });

  it('records failures so they are retried, not marked handled', async () => {
    sendMail.mockRejectedValueOnce(new Error('SMTP down'));
    const e = envelope({ orderId, email: 'casey@example.com', total: 1 }, 'evt-3');
    await expect(service.handle(TOPICS.orderConfirmed, e)).rejects.toThrow('SMTP down');
    expect(mailer.recent()[0]).toMatchObject({ status: 'failed', error: 'SMTP down' });
    await expect(service.handle(TOPICS.orderConfirmed, e)).resolves.toBe(true);
  });
});

describe('parseEnvelope', () => {
  it('rejects junk', () => {
    expect(() => parseEnvelope(Buffer.from('nope'))).toThrow();
    expect(() => parseEnvelope(Buffer.from('{}'))).toThrow();
    expect(parseEnvelope(Buffer.from(JSON.stringify(envelope({ orderId })))).payload).toEqual({ orderId });
  });
});
