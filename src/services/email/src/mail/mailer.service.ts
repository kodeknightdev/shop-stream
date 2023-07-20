import { Inject, Injectable, Logger } from '@nestjs/common';
import { createTransport, Transporter } from 'nodemailer';
import { APP_CONFIG, AppConfig } from '../config';

export interface SentEmail {
  id: string;
  to: string;
  subject: string;
  template: string;
  orderId?: string;
  sentAt: string;
  status: 'sent' | 'failed';
  error?: string;
}

export const MAIL_TRANSPORT = Symbol('MAIL_TRANSPORT');

export const mailTransportProvider = {
  provide: MAIL_TRANSPORT,
  inject: [APP_CONFIG],
  useFactory: (config: AppConfig): Transporter =>
    createTransport({ host: config.smtp.host, port: config.smtp.port, secure: false, ignoreTLS: true }),
};

// keeps the last 100 sends in memory for the admin endpoint
@Injectable()
export class MailerService {
  private readonly logger = new Logger(MailerService.name);
  private readonly sent: SentEmail[] = [];

  constructor(@Inject(MAIL_TRANSPORT) private readonly transport: Transporter, @Inject(APP_CONFIG) private readonly config: AppConfig) {}

  async send(to: string, subject: string, html: string, template: string, orderId?: string): Promise<SentEmail> {
    const record: SentEmail = { id: '', to, subject, template, orderId, sentAt: new Date().toISOString(), status: 'sent' };
    try {
      const info = await this.transport.sendMail({ from: this.config.smtp.from, to, subject, html });
      record.id = info.messageId;
      this.logger.log(`Sent ${template} to ${to} (${info.messageId})`);
      return record;
    } catch (err) {
      record.status = 'failed';
      record.error = (err as Error).message;
      throw err;
    } finally {
      this.sent.unshift(record);
      this.sent.length = Math.min(this.sent.length, 100);
    }
  }

  recent(limit = 25): SentEmail[] {
    return this.sent.slice(0, limit);
  }

  async verify(): Promise<boolean> {
    try {
      return await this.transport.verify();
    } catch {
      return false;
    }
  }
}
