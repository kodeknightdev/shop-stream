import { MiddlewareConsumer, Module, NestModule } from '@nestjs/common';
import { APP_FILTER } from '@nestjs/core';
import { CorrelationIdMiddleware } from './common/correlation';
import { ProblemDetailsFilter } from './common/problem-details.filter';
import { APP_CONFIG, config } from './config';
import { InboxService, inboxRedisProvider } from './inbox/inbox.service';
import { mailTransportProvider, MailerService } from './mail/mailer.service';
import { TemplatesService } from './mail/templates.service';
import { NotificationsService } from './notifications/notifications.service';

@Module({
  providers: [
    { provide: APP_CONFIG, useValue: config },
    { provide: APP_FILTER, useClass: ProblemDetailsFilter },
    mailTransportProvider,
    inboxRedisProvider,
    InboxService,
    MailerService,
    TemplatesService,
    NotificationsService,
  ],
})
export class AppModule implements NestModule {
  configure(consumer: MiddlewareConsumer) {
    consumer.apply(CorrelationIdMiddleware).forRoutes('*');
  }
}
