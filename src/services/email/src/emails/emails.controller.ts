import { Body, Controller, Get, Header, ParseIntPipe, Post, Query, UseGuards, DefaultValuePipe } from '@nestjs/common';
import { ApiBearerAuth, ApiOkResponse, ApiTags } from '@nestjs/swagger';
import { AdminGuard } from '../common/admin.guard';
import { MailerService } from '../mail/mailer.service';
import { TemplatesService } from '../mail/templates.service';
import { PreviewEmailDto } from './preview.dto';

@ApiTags('emails')
@ApiBearerAuth()
@UseGuards(AdminGuard)
@Controller('api/emails')
export class EmailsController {
  constructor(private readonly mailer: MailerService, private readonly templates: TemplatesService) {}

  @Get('recent')
  @ApiOkResponse({ description: 'Most recent sends (in-memory, newest first)' })
  recent(@Query('limit', new DefaultValuePipe(25), ParseIntPipe) limit: number) {
    return this.mailer.recent(Math.min(Math.max(limit, 1), 100));
  }

  @Get('templates')
  list() {
    return this.templates.names();
  }

  @Post('preview')
  @Header('Content-Type', 'text/html; charset=utf-8')
  preview(@Body() dto: PreviewEmailDto) {
    const data = { orderNumber: 'PREVIEW', orderUrl: '#', shopUrl: '#', items: [], total: 0, currency: 'USD', reason: 'Example reason', ...dto.data };
    return this.templates.render(dto.template, `Preview: ${dto.template}`, data);
  }
}
