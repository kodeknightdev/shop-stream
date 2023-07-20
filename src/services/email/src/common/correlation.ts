import { AsyncLocalStorage } from 'async_hooks';
import { randomUUID } from 'crypto';
import { Injectable, NestMiddleware } from '@nestjs/common';
import type { NextFunction, Request, Response } from 'express';

const storage = new AsyncLocalStorage<string>();

export const currentCorrelationId = () => storage.getStore();
export const runWithCorrelationId = <T>(id: string, fn: () => T) => storage.run(id, fn);

@Injectable()
export class CorrelationIdMiddleware implements NestMiddleware {
  use(req: Request, res: Response, next: NextFunction) {
    const id = req.header('x-correlation-id') || randomUUID().replace(/-/g, '');
    (req as Request & { correlationId: string }).correlationId = id;
    res.setHeader('X-Correlation-ID', id);
    storage.run(id, () => next());
  }
}
