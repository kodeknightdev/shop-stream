import 'reflect-metadata';
import { ValidationPipe } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { AppModule } from './app.module';
import { JsonLogger } from './common/logger';
import { config } from './config';

async function bootstrap() {
  if (!config.jwt.signingKey) throw new Error('JWT_SIGNING_KEY must be set');

  const app = await NestFactory.create(AppModule, { logger: new JsonLogger() });
  app.useGlobalPipes(new ValidationPipe({ whitelist: true, forbidNonWhitelisted: true, transform: true }));
  app.enableShutdownHooks();

  const document = SwaggerModule.createDocument(
    app,
    new DocumentBuilder().setTitle('shop-stream email').setDescription('Order notification emails').setVersion('1.0.0').addBearerAuth().build(),
  );
  SwaggerModule.setup('api-docs', app, document);

  await app.listen(config.port);
}

bootstrap();
