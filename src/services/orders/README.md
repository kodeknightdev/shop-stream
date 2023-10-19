# orders

Owns the order lifecycle and drives the checkout saga.

```
Pending ──stock-reserved──▶ InventoryReserved ──payment-succeeded──▶ Paid ──▶ Confirmed
   │                              │
   └──stock-rejected / cancel──▶ Cancelled ◀── payment-failed / expiry / cancel
```

Placing an order:
1. Reads the caller's cart from the cart service (REST, forwarding the caller's token).
2. Fetches authoritative prices from catalog over **gRPC**. Prices always come from the catalog; differences from the cart are returned as `priceAdjustments`.
3. Stores the order and an `orders.order-placed` outbox row in one transaction.

Every outgoing call (catalog gRPC, cart and payments REST) goes through a Polly stack - bulkhead →
retry → circuit breaker → timeout ([ResiliencePolicies.cs](ShopStream.Orders/Clients/ResiliencePolicies.cs)).
An open circuit or rejected call becomes a quick 503.

`GET /api/orders/{id}/details` is an **API composition**: the order from this database, plus the
payment record (payments, REST) and current product snapshots (catalog, gRPC), fetched in parallel.
A dependency that's down becomes a `warnings` entry.

## Endpoints

| Method | Path | Auth | Notes |
|---|---|---|---|
| POST | `/api/orders` | user | body: `shippingAddress`, `paymentMethod` (`pm_card_*`); optional `Idempotency-Key` header |
| GET | `/api/orders?page=` | user | own orders |
| GET | `/api/orders/{id}` | owner / Admin | detail + status timeline |
| GET | `/api/orders/{id}/details` | owner / Admin | order + payment + product snapshots + warnings |
| POST | `/api/orders/{id}/cancel` | owner / Admin | only while Pending / InventoryReserved |
| GET | `/api/admin/orders` | Admin | `status`, `email`, `from`, `to`, `page` |
| GET | `/api/admin/sales-summary?days=14` | Admin | orders and confirmed revenue per day |
| GET | `/health`, `/health/ready` | - | Postgres + Kafka |

## Kafka

- Produces (via the shared outbox relay): `orders.order-placed`, `orders.order-confirmed`, `orders.order-cancelled`
- Consumes (group `orders`): `inventory.stock-reserved`, `inventory.stock-rejected`, `payments.payment-succeeded`, `payments.payment-failed`, and the `.retry` topic of each
- Idempotent: every handled event id goes into `processed_events` in the same `SaveChanges` as the state change
- Failures: `<topic>.retry` with exponential backoff, then `<topic>.dlq` after `Kafka__MaxRetries` (5)

A `payment-succeeded` that arrives before `stock-reserved` throws on purpose, so it goes through the
retry topic and is applied once the reservation has been processed.

## Data

`orders_db`: `orders`, `order_items`, `order_status_history`, `outbox_messages`, `processed_events`,
`idempotency_keys`. In Development, [DemoOrderSeeder](ShopStream.Orders/Data/DemoOrderSeeder.cs)
gives `jane@shopstream.local` three past orders (two confirmed, one cancelled after a declined card).

## Run

```bash
dotnet run --project src/services/orders/ShopStream.Orders   # http://localhost:5003
```

Config: `ConnectionStrings__OrdersDb`, `Services__CatalogGrpc`, `Services__Cart`, `Services__Payments`,
`Kafka__*`, `Outbox__*`, `Resilience__*`.
