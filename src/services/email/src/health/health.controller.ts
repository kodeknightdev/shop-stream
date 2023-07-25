import { Controller, Get, Res } from '@nestjs/common';
import { ApiTags } from '@nestjs/swagger';
import type { Response } from 'express';
import { InboxService } from '../inbox/inbox.service';
import { KafkaConsumerService } from '../kafka/kafka-consumer.service';
import { MailerService } from '../mail/mailer.service';

@ApiTags('health')
@Controller('health')
export class HealthController {
  constructor(private readonly mailer: MailerService, private readonly kafka: KafkaConsumerService, private readonly inbox: InboxService) {}

  @Get()
  live() {
    return { status: 'Healthy' };
  }

  @Get('ready')
  async ready(@Res() res: Response) {
    const checks = {
      smtp: (await this.mailer.verify()) ? 'Healthy' : 'Unhealthy',
      kafka: this.kafka.isConnected ? 'Healthy' : 'Unhealthy',
      redis: (await this.inbox.ping()) ? 'Healthy' : 'Unhealthy',
    };
    const healthy = Object.values(checks).every((v) => v === 'Healthy');
    res.status(healthy ? 200 : 503).json({ status: healthy ? 'Healthy' : 'Unhealthy', checks });
  }
}
