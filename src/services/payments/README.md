# payments

Charges an order once its stock is reserved. Two providers:

- **Stripe (test mode)** - when `Stripe__SecretKey` is an `sk_test_...` key. Creates and confirms a
  PaymentIntent with the payment method chosen at checkout (`pm_card_visa`,
  `pm_card_chargeDeclined`, ...). Any key that isn't a test key stops the service at startup.
- **Mock** - when no key is set. Understands the same test payment method ids, declines
  `pm_card_chargeDeclined` / `..._chargeDeclinedInsufficientFunds`, and refuses amounts over 5,000.

Exactly one payment record per order (unique index on `OrderId`). The Stripe call uses the
idempotency key `shop-stream-order-<orderId>`; a redelivered event re-publishes the stored outcome
instead of charging again.

## Endpoints

| Method | Path | Auth |
|---|---|---|
| GET | `/api/payments/orders/{orderId}` | owner / Admin |
| GET | `/api/payments?status=` | Admin |
| GET | `/api/payments/provider` | - (`mock` or `stripe`) |
| GET | `/health`, `/health/ready` | - |

## Kafka

- Consumes (group `payments`): `inventory.stock-reserved` and its `.retry` topic
- Produces: `payments.payment-succeeded`, `payments.payment-failed`

## Run

```bash
dotnet run --project src/services/payments/ShopStream.Payments   # http://localhost:5004
# with Stripe test mode:
Stripe__SecretKey=sk_test_... dotnet run --project src/services/payments/ShopStream.Payments
```
