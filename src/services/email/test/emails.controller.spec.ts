import { INestApplication, ValidationPipe } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import * as jwt from 'jsonwebtoken';
import request from 'supertest';
import { AppModule } from '../src/app.module';
import { APP_CONFIG, config } from '../src/config';
import { MAIL_TRANSPORT } from '../src/mail/mailer.service';
import { INBOX_REDIS } from '../src/inbox/inbox.service';
import RedisMock from 'ioredis-mock';

const signingKey = 'test-signing-key-for-email-tests-0000000000';
const tokenFor = (role: string) =>
  jwt.sign({ sub: 'u1', email: 'a@b.c', role }, signingKey, { issuer: 'shop-stream-identity', audience: 'shop-stream', expiresIn: '5m' });

describe('EmailsController', () => {
  let app: INestApplication;

  beforeAll(async () => {
    const moduleRef = await Test.createTestingModule({ imports: [AppModule] })
      .overrideProvider(APP_CONFIG).useValue({ ...config, jwt: { ...config.jwt, signingKey }, kafka: { ...config.kafka, enabled: false } })
      .overrideProvider(MAIL_TRANSPORT).useValue({ sendMail: jest.fn(), verify: jest.fn().mockResolvedValue(true) })
      .overrideProvider(INBOX_REDIS).useValue(new RedisMock())
      .compile();
    app = moduleRef.createNestApplication();
    app.useGlobalPipes(new ValidationPipe({ whitelist: true, forbidNonWhitelisted: true }));
    await app.init();
  });

  afterAll(() => app.close());

  it('requires an admin token', async () => {
    await request(app.getHttpServer()).get('/api/emails/templates').expect(401);
    await request(app.getHttpServer()).get('/api/emails/templates').set('Authorization', `Bearer ${tokenFor('Customer')}`).expect(403);
  });

  it('lists templates', async () => {
    const res = await request(app.getHttpServer()).get('/api/emails/templates').set('Authorization', `Bearer ${tokenFor('Admin')}`).expect(200);
    expect(res.body).toEqual(['order-cancelled', 'order-confirmed', 'order-placed', 'payment-failed']);
  });

  it('validates the preview body as problem details', async () => {
    const res = await request(app.getHttpServer())
      .post('/api/emails/preview')
      .set('Authorization', `Bearer ${tokenFor('Admin')}`)
      .send({ template: 'nope' })
      .expect(400);
    expect(res.headers['content-type']).toMatch(/application\/problem\+json/);
    expect(res.body.errors[0]).toContain('template');
  });

  it('renders a preview', async () => {
    const res = await request(app.getHttpServer())
      .post('/api/emails/preview')
      .set('Authorization', `Bearer ${tokenFor('Admin')}`)
      .send({ template: 'order-cancelled', data: { reason: 'Out of stock' } })
      .expect(201);
    expect(res.text).toContain('Out of stock');
  });

  it('reports health', async () => {
    await request(app.getHttpServer()).get('/health').expect(200, { status: 'Healthy' });
    const ready = await request(app.getHttpServer()).get('/health/ready').expect(200);
    expect(ready.body.checks).toEqual({ smtp: 'Healthy', kafka: 'Healthy', redis: 'Healthy' });
  });
});
