import { Inject, Injectable, Logger } from '@nestjs/common';
import { APP_CONFIG, AppConfig } from '../config';
import { InboxService } from '../inbox/inbox.service';
import { MailerService } from '../mail/mailer.service';
import { TemplateName, TemplatesService } from '../mail/templates.service';

export const TOPICS = {
  orderPlaced: 'orders.order-placed',
  orderConfirmed: 'orders.order-confirmed',
  orderCancelled: 'orders.order-cancelled',
  paymentFailed: 'payments.payment-failed',
} as const;

export interface Envelope<T = Record<string, any>> {
  eventId: string;
  eventType: string;
  schemaVersion: number;
  occurredAt: string;
  correlationId?: string;
  payload: T;
}

const shortRef = (orderId: string) => orderId.slice(0, 8).toUpperCase();

@Injectable()
export class NotificationsService {
  private readonly logger = new Logger(NotificationsService.name);
  constructor(
    private readonly templates: TemplatesService,
    private readonly mailer: MailerService,
    private readonly inbox: InboxService,
    @Inject(APP_CONFIG) private readonly config: AppConfig,
  ) {}

  plan(topic: string, envelope: Envelope): { template: TemplateName; subject: string; to: string; data: Record<string, unknown> } | null {
    const p = envelope.payload;
    const orderId = String(p.orderId ?? '');
    const common = { orderNumber: shortRef(orderId), orderUrl: `${this.config.shopUrl}/orders/${orderId}`, shopUrl: this.config.shopUrl };

    switch (topic) {
      case TOPICS.orderPlaced:
        return { template: 'order-placed', subject: `We received your order ${common.orderNumber}`, to: p.email, data: { ...common, ...p } };
      case TOPICS.orderConfirmed:
        return { template: 'order-confirmed', subject: `Order ${common.orderNumber} is confirmed`, to: p.email, data: { ...common, ...p } };
      case TOPICS.orderCancelled:
        return { template: 'order-cancelled', subject: `Order ${common.orderNumber} was cancelled`, to: p.email, data: { ...common, ...p } };
      case TOPICS.paymentFailed:
        return { template: 'payment-failed', subject: `Payment failed for order ${common.orderNumber}`, to: p.email, data: { ...common, ...p } };
      default:
        return null;
    }
  }

  async handle(topic: string, envelope: Envelope): Promise<boolean> {
    if (await this.inbox.seen(envelope.eventId)) {
      this.logger.log(`Skipping duplicate event ${envelope.eventId}`);
      return false;
    }

    const plan = this.plan(topic, envelope);
    if (!plan) return false;
    if (!plan.to) {
      this.logger.warn(`Event ${envelope.eventId} on ${topic} has no recipient; skipping`);
      return false;
    }

    await this.inbox.record(envelope.eventId, envelope.eventType);
    const html = this.templates.render(plan.template, plan.subject, plan.data);
    await this.mailer.send(plan.to, plan.subject, html, plan.template, String(envelope.payload.orderId ?? ''));
    return true;
  }
}
