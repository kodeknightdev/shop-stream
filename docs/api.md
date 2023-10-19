# API

Everything is reached through the gateway at `http://localhost:5000` (or `GATEWAY_PORT`). Errors are
always RFC 7807 `application/problem+json` with a `correlationId`. Authenticated calls take
`Authorization: Bearer <access token>` from `/api/auth/login`.

## OpenAPI / Swagger

| Service | Through the gateway | Directly (dev override ports) |
|---|---|---|
| all services, one UI | http://localhost:5000/swagger | - |
| identity | `/docs/identity/swagger.json` | http://localhost:5001/swagger |
| catalog | `/docs/catalog/swagger.json` | http://localhost:5002/swagger |
| orders | `/docs/orders/swagger.json` | http://localhost:5003/swagger |
| payments | `/docs/payments/swagger.json` | http://localhost:5004/swagger |
| inventory | `/docs/inventory/openapi.json` | http://localhost:8001/docs |
| cart | `/docs/cart/openapi.json` | http://localhost:3001/api-docs |
| email | `/docs/email/openapi.json` | http://localhost:3002/api-docs |

## identity

| Method | Path | Auth | Notes |
|---|---|---|---|
| POST | `/api/auth/register` | - | creates a Customer, returns tokens |
| POST | `/api/auth/login` | - | rate limited (10/min per IP) |
| POST | `/api/auth/refresh` | - | rotates the refresh token; replaying an old one revokes the family |
| POST | `/api/auth/logout` | - | revokes the refresh token |
| GET | `/api/auth/me` | user | |
| GET | `/api/users?search=&page=` | Admin | |
| PUT | `/api/users/{id}/role` | Admin | `Admin` or `Customer` |

## catalog

| Method | Path | Auth | Notes |
|---|---|---|---|
| GET | `/api/categories` | - | with product counts |
| POST / PUT | `/api/categories[/{id}]` | Admin | |
| GET | `/api/products` | - | `search`, `category` (slug or id), `minPrice`, `maxPrice`, `sort` (`name`, `name_desc`, `price_asc`, `price_desc`, `newest`), `page`, `pageSize` ≤ 60 |
| GET | `/api/products/{id}` | - | cache-aside; response header `X-Cache: HIT\|MISS` |
| GET | `/api/products/by-slug/{slug}` | - | cache-aside |
| POST | `/api/products` | Admin | |
| PUT | `/api/products/{id}` | Admin | invalidates the cache; a price change publishes `catalog.product-price-changed` |
| DELETE | `/api/products/{id}` | Admin | soft delete |

### gRPC - `shared/protos/catalog.proto`

Served by catalog on port 8081 (HTTP/2 only), consumed by orders.

| RPC | Request → Reply | Notes |
|---|---|---|
| `GetProduct` | `product_id` → `ProductReply` | `NotFound` / `InvalidArgument` status codes |
| `GetProductsByIds` | `product_ids[]` (≤ 200) → `products[]`, `missing_ids[]` | prices as `price_cents` (int64) |

## cart

| Method | Path | Auth | Notes |
|---|---|---|---|
| GET | `/api/cart` | user | items, `itemCount`, `subtotal`, `hasStalePrices` |
| POST | `/api/cart/items` | user | `{ productId, quantity }`; price looked up in catalog |
| PUT | `/api/cart/items/{productId}` | user | `{ quantity }`, 0 removes |
| DELETE | `/api/cart/items/{productId}` | user | |
| DELETE | `/api/cart` | user | used by orders after checkout |
| POST | `/api/cart/validate` | user | re-reads every line from catalog |

## orders

| Method | Path | Auth | Notes |
|---|---|---|---|
| POST | `/api/orders` | user | `{ shippingAddress, paymentMethod }` with `pm_card_*`; optional `Idempotency-Key` header → 201, or 200 for a replay |
| GET | `/api/orders?page=` | user | own orders |
| GET | `/api/orders/{id}` | owner / Admin | order with status timeline |
| GET | `/api/orders/{id}/details` | owner / Admin | **API composition**: `{ order, payment, products, warnings }` |
| POST | `/api/orders/{id}/cancel` | owner / Admin | while Pending / InventoryReserved |
| GET | `/api/admin/orders` | Admin | `status`, `email`, `from`, `to`, `page` |
| GET | `/api/admin/sales-summary?days=14` | Admin | orders and confirmed revenue per day |

## payments

| Method | Path | Auth |
|---|---|---|
| GET | `/api/payments/orders/{orderId}` | owner / Admin |
| GET | `/api/payments?status=` | Admin |
| GET | `/api/payments/provider` | - (`mock` or `stripe`) |

## inventory

| Method | Path | Auth |
|---|---|---|
| GET | `/api/inventory/{productId}` | - (`available`, `inStock`, `lowStock`; cached 15 s) |
| POST | `/api/inventory/availability` `{ productIds }` | - |
| GET | `/api/inventory?lowStock=&page=&pageSize=` | Admin |
| POST | `/api/inventory/{productId}/adjust` `{ delta, reason, sku?, lowStockThreshold? }` | Admin |
| GET | `/api/inventory/reservations?status=` | Admin |

## email

| Method | Path | Auth |
|---|---|---|
| GET | `/api/emails/recent?limit=` | Admin |
| GET | `/api/emails/templates` | Admin |
| POST | `/api/emails/preview` `{ template, data }` → HTML | Admin |

## Health (every service)

`GET /health` - liveness, no dependencies. `GET /health/ready` - readiness with per-dependency
checks. The gateway's readiness pings every downstream service and reports `Degraded` when one is
down.

## Events

The Kafka topics and payloads are listed in [architecture.md](architecture.md#event-and-topic-catalog).
