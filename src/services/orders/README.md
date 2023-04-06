# orders

Owns the order lifecycle and drives the checkout saga.

```
Pending ──stock-reserved──▶ InventoryReserved ──payment-succeeded──▶ Paid ──▶ Confirmed
   │                              │
   └──stock-rejected / cancel──▶ Cancelled ◀── payment-failed / cancel
```

Placing an order reads the caller's cart from the cart service (REST, forwarding the caller's token),
fetches authoritative prices from catalog over **gRPC**, and stores the order plus an
`orders.order-placed` outbox row in one transaction. Prices always come from the catalog; differences
from the cart are returned as `priceAdjustments`.

Every outgoing call goes through a Polly stack - bulkhead → retry → circuit breaker → timeout
(`Clients/ResiliencePolicies.cs`); an open circuit becomes a quick 503.

## Endpoints

| Method | Path | Auth | Notes |
|---|---|---|---|
| POST | `/api/orders` | user | body: `shippingAddress`, `paymentMethod` (`pm_card_*`); optional `Idempotency-Key` header |
| GET | `/api/orders?page=` | user | own orders |
| GET | `/api/orders/{id}` | owner / Admin | detail + status timeline |
| POST | `/api/orders/{id}/cancel` | owner / Admin | only while Pending / InventoryReserved |
| GET | `/api/admin/orders` | Admin | `status`, `email`, `from`, `to`, `page` |
| GET | `/api/admin/sales-summary?days=14` | Admin | orders and confirmed revenue per day |
| GET | `/health`, `/health/ready` | - | Postgres + Kafka |

## Kafka

- Produces (via outbox): `orders.order-placed`, `orders.order-confirmed`, `orders.order-cancelled`
- Consumes (group `orders`): `inventory.stock-reserved`, `inventory.stock-rejected`,
  `payments.payment-succeeded`, `payments.payment-failed`, and the `.retry` topic of each
- Failures: `<topic>.retry` (exponential backoff) → `<topic>.dlq` after `Kafka__MaxRetries` (5)

A `payment-succeeded` that arrives before `stock-reserved` throws on purpose so it goes through the
retry topic and is applied once the reservation has been processed.

## Data

`orders_db`: `orders`, `order_items`, `order_status_history`, `outbox_messages`,
`processed_events`, `idempotency_keys`.

## Run

```bash
dotnet run --project src/services/orders/ShopStream.Orders   # http://localhost:5003
```

Config: `ConnectionStrings__OrdersDb`, `Services__CatalogGrpc`, `Services__Cart`, `Kafka__*`, `Outbox__PollIntervalMs`.
