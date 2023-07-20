import { ArgumentsHost, BadRequestException, Catch, ExceptionFilter, HttpException, HttpStatus, Logger } from '@nestjs/common';
import type { Request, Response } from 'express';
import { currentCorrelationId } from './correlation';

@Catch()
export class ProblemDetailsFilter implements ExceptionFilter {
  private readonly logger = new Logger('Errors');

  catch(exception: unknown, host: ArgumentsHost) {
    const ctx = host.switchToHttp();
    const req = ctx.getRequest<Request>();
    const res = ctx.getResponse<Response>();

    const status = exception instanceof HttpException ? exception.getStatus() : HttpStatus.INTERNAL_SERVER_ERROR;
    const body: Record<string, unknown> = {
      type: `https://httpstatuses.io/${status}`,
      title: exception instanceof HttpException ? exception.message : 'An unexpected error occurred',
      status,
      instance: req.originalUrl,
      correlationId: currentCorrelationId(),
    };

    if (exception instanceof BadRequestException) {
      const response = exception.getResponse() as { message?: string | string[] };
      body.title = 'One or more validation errors occurred.';
      body.errors = Array.isArray(response.message) ? response.message : [response.message];
    }

    if (status >= 500) {
      this.logger.error(exception instanceof Error ? exception.message : String(exception), exception instanceof Error ? exception.stack : undefined);
    }

    res.status(status).type('application/problem+json').send(body);
  }
}
