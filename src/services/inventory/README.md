# inventory (Python)

Stock levels and reservations. FastAPI + SQLAlchemy 2.0 on PostgreSQL, Redis for a short availability
cache (cache-aside, 15 s), confluent-kafka for events.

- `available = on_hand - reserved`; `lowStock` when available ≤ threshold (5 by default)
- An order reserves **all or nothing** inside one transaction with row locks (`SELECT ... FOR UPDATE`)
- Reservations expire after `RESERVATION_TTL_SECONDS` (900); a background sweep releases them and
  publishes `stock-rejected` so the order is cancelled
- Payment success commits the reservation (on hand decreases); cancellation releases it

## Endpoints

| Method | Path | Auth |
|---|---|---|
| GET | `/api/inventory/{productId}` | - |
| POST | `/api/inventory/availability` `{ productIds: [] }` | - |
| GET | `/api/inventory?lowStock=&page=&pageSize=` | Admin |
| POST | `/api/inventory/{productId}/adjust` `{ delta, reason, sku?, lowStockThreshold? }` | Admin |
| GET | `/api/inventory/reservations?status=` | Admin |
| GET | `/health`, `/health/ready` (Postgres, Redis, Kafka) | - |

OpenAPI: `/docs` and `/openapi.json`. Errors are `application/problem+json`.

## Kafka

- Consumes (group `inventory`): `orders.order-placed`, `orders.order-cancelled`,
  `payments.payment-succeeded`, and the `.retry` topic of each
- Produces: `inventory.stock-reserved`, `inventory.stock-rejected`
- Idempotent: handled event ids go into the `processed_events` table in the same transaction as the
  stock change (`InventoryService._commit`)
- Failures: `<topic>.retry` with exponential backoff ([retry.py](app/retry.py)), then `<topic>.dlq`

## Run

```bash
cd src/services/inventory
python3.11 -m venv .venv && . .venv/bin/activate
pip install -r requirements.txt
export JWT_SIGNING_KEY=local-dev-signing-key-not-for-production-0001
uvicorn app.main:create_app --factory --reload --port 8001
pytest
```

Settings (env vars): `DATABASE_URL`, `REDIS_URL`, `KAFKA_ENABLED`, `KAFKA_BOOTSTRAP_SERVERS`,
`JWT_SIGNING_KEY`, `RESERVATION_TTL_SECONDS`, `EXPIRY_INTERVAL_SECONDS`, `SEED_ON_STARTUP`, `LOG_FORMAT`.
