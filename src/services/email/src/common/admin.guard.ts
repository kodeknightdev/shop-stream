import { CanActivate, ExecutionContext, ForbiddenException, Inject, Injectable, UnauthorizedException } from '@nestjs/common';
import * as jwt from 'jsonwebtoken';
import type { Request } from 'express';
import { APP_CONFIG, AppConfig } from '../config';

@Injectable()
export class AdminGuard implements CanActivate {
  constructor(@Inject(APP_CONFIG) private readonly config: AppConfig) {}

  canActivate(context: ExecutionContext): boolean {
    const req = context.switchToHttp().getRequest<Request>();
    const [scheme, token] = (req.header('authorization') ?? '').split(' ');
    if (scheme?.toLowerCase() !== 'bearer' || !token) throw new UnauthorizedException('A bearer token is required');

    let claims: jwt.JwtPayload;
    try {
      claims = jwt.verify(token, this.config.jwt.signingKey, {
        algorithms: ['HS256'],
        issuer: this.config.jwt.issuer,
        audience: this.config.jwt.audience,
        clockTolerance: 30,
      }) as jwt.JwtPayload;
    } catch {
      throw new UnauthorizedException('Invalid or expired token');
    }
    if (claims.role !== 'Admin') throw new ForbiddenException('Admin role required');
    return true;
  }
}
