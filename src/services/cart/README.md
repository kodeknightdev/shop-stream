# cart (Node / Express)

Per-user carts in Redis. Each cart is one JSON document under `cart:<userId>` with a sliding 7-day
TTL; a reverse index `cart:product:<productId>` lets the price consumer find affected carts.

Prices are copied from the catalog when an item is added. When the catalog publishes a price change
the line is **flagged** (`priceStale`, `latestPrice`) rather than silently changed;
`POST /api/cart/validate` re-reads every line from the catalog. Orders re-validate prices again over
gRPC at checkout, so the cart is never the source of truth for money.

## Endpoints (all require a bearer token)

| Method | Path | Notes |
|---|---|---|
| GET | `/api/cart` | items, itemCount, subtotal, hasStalePrices |
| POST | `/api/cart/items` | `{ productId, quantity }` - merges, max 20 per line |
| PUT | `/api/cart/items/:productId` | `{ quantity }` - 0 removes |
| DELETE | `/api/cart/items/:productId` | |
| DELETE | `/api/cart` | clear (used by orders after checkout) |
| POST | `/api/cart/validate` | apply current catalog prices |
| GET | `/health`, `/health/ready` | Redis + Kafka |

Validation with zod; errors are `application/problem+json`. OpenAPI at `/api-docs`
(`/api-docs/openapi.json`).

Calls to catalog go through a cockatiel policy - bulkhead → retry → circuit breaker → timeout - in
[catalogClient.js](src/catalogClient.js). While catalog is down, adds fail fast with a 503.

## Kafka

- Consumes (group `cart`): `catalog.product-price-changed` and its `.retry` topic. The event carries the
  new price, so carts are flagged without calling catalog back (event-carried state transfer)
- Idempotent: handled event ids are kept in Redis under `cart:processed_events:<id>` ([inbox.js](src/inbox.js))
- Failures: `catalog.product-price-changed.retry` with backoff, then `catalog.product-price-changed.dlq`

## Run

```bash
cd src/services/cart
npm ci
JWT_SIGNING_KEY=local-dev-signing-key-not-for-production-0001 npm run dev   # :3001
npm test
```

Env: `PORT`, `REDIS_URL`, `CART_TTL_SECONDS`, `CATALOG_URL`, `KAFKA_ENABLED`, `KAFKA_BOOTSTRAP_SERVERS`, `JWT_SIGNING_KEY`.
