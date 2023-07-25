import { MiddlewareConsumer, Module, NestModule } from '@nestjs/common';
import { APP_FILTER } from '@nestjs/core';
import { CorrelationIdMiddleware } from './common/correlation';
import { ProblemDetailsFilter } from './common/problem-details.filter';
import { APP_CONFIG, config } from './config';
import { HealthController } from './health/health.controller';
import { InboxService, inboxRedisProvider } from './inbox/inbox.service';
import { KafkaConsumerService } from './kafka/kafka-consumer.service';
import { mailTransportProvider, MailerService } from './mail/mailer.service';
import { TemplatesService } from './mail/templates.service';
import { NotificationsService } from './notifications/notifications.service';

@Module({
  controllers: [HealthController],
  providers: [
    { provide: APP_CONFIG, useValue: config },
    { provide: APP_FILTER, useClass: ProblemDetailsFilter },
    mailTransportProvider,
    inboxRedisProvider,
    InboxService,
    MailerService,
    TemplatesService,
    NotificationsService,
    KafkaConsumerService,
  ],
})
export class AppModule implements NestModule {
  configure(consumer: MiddlewareConsumer) {
    consumer.apply(CorrelationIdMiddleware).forRoutes('*');
  }
}
