import { ApiProperty } from '@nestjs/swagger';
import { IsIn, IsNotEmpty, IsObject, IsOptional } from 'class-validator';
import { TEMPLATE_NAMES } from '../mail/templates.service';

export class PreviewEmailDto {
  @ApiProperty({ enum: TEMPLATE_NAMES, example: 'order-confirmed' })
  @IsNotEmpty()
  @IsIn(TEMPLATE_NAMES)
  template!: string;

  @ApiProperty({ required: false, example: { orderId: '7f1c1f9e-0000-0000-0000-000000000000', total: 42.5, currency: 'USD' } })
  @IsOptional()
  @IsObject()
  data?: Record<string, unknown>;
}
