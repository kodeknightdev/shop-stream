import { LoggerService } from '@nestjs/common';
import { currentCorrelationId } from './correlation';

// same field names as the .NET services
export class JsonLogger implements LoggerService {
  private write(level: string, message: unknown, context?: string, extra?: Record<string, unknown>) {
    if (process.env.NODE_ENV === 'test') return;
    const line = {
      '@t': new Date().toISOString(),
      '@l': level,
      '@m': typeof message === 'string' ? message : JSON.stringify(message),
      Service: 'email',
      SourceContext: context,
      CorrelationId: currentCorrelationId(),
      ...extra,
    };
    process.stdout.write(`${JSON.stringify(line)}\n`);
  }

  log(message: unknown, context?: string) { this.write('Information', message, context); }
  error(message: unknown, trace?: string, context?: string) { this.write('Error', message, context, trace ? { '@x': trace } : undefined); }
  warn(message: unknown, context?: string) { this.write('Warning', message, context); }
  debug(message: unknown, context?: string) { this.write('Debug', message, context); }
  verbose(message: unknown, context?: string) { this.write('Verbose', message, context); }
}
