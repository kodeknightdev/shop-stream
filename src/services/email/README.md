# email (Node / NestJS)

Turns saga events into customer emails. Templates are Handlebars files in `templates/` wrapped in a
shared `layout.hbs`; mail goes out through nodemailer to MailHog locally (any SMTP server elsewhere).
The service keeps no database - the last 100 sends are kept in memory and everything is logged.

| Event | Template | Subject |
|---|---|---|
| `orders.order-placed` | `order-placed.hbs` | We received your order … |
| `orders.order-confirmed` | `order-confirmed.hbs` | Order … is confirmed |
| `orders.order-cancelled` | `order-cancelled.hbs` | Order … was cancelled |
| `payments.payment-failed` | `payment-failed.hbs` | Payment failed for order … |

## Endpoints

| Method | Path | Auth |
|---|---|---|
| GET | `/api/emails/recent?limit=` | Admin |
| GET | `/api/emails/templates` | Admin |
| POST | `/api/emails/preview` `{ template, data }` → HTML | Admin |
| GET | `/health`, `/health/ready` (SMTP + Kafka) | - |

Validation via class-validator; errors are problem details. Swagger at `/api-docs`.

## Kafka

- Consumes (group `email`): the four topics above and their `.retry` topics
- SMTP failures go to `<topic>.retry` with backoff, then `<topic>.dlq`; duplicate event ids are skipped

## Run

```bash
cd src/services/email
npm ci
npm run build
JWT_SIGNING_KEY=local-dev-signing-key-not-for-production-0001 npm start   # :3002
npm test
```

Env: `PORT`, `SMTP_HOST`, `SMTP_PORT`, `MAIL_FROM`, `SHOP_URL`, `KAFKA_ENABLED`, `KAFKA_BOOTSTRAP_SERVERS`, `JWT_SIGNING_KEY`.
