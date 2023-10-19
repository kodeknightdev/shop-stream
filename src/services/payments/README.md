# payments

Charges an order once its stock is reserved. The payment logic only talks to `IPaymentProvider`, an
**anti-corruption layer** with two implementations:

- **Stripe (test mode)** - used when `Stripe__SecretKey` is an `sk_test_...` key. Creates and confirms a PaymentIntent with the payment method chosen at checkout (`pm_card_visa`, `pm_card_chargeDeclined`, ...). Any key that isn't a test key stops the service at startup. Only [StripePaymentProvider.cs](ShopStream.Payments/Providers/StripePaymentProvider.cs) references Stripe types.
- **Mock** - used when no key is set. Understands the same test payment method ids, declines `pm_card_chargeDeclined` / `..._chargeDeclinedInsufficientFunds`, and refuses amounts over 5,000.

Exactly one payment per order:
- The **order id is the Stripe idempotency key**.
- A unique index on `OrderId` prevents a second record.
- The `processed_events` inbox skips a redelivered event.

The payment record, the inbox row and the outcome event (in `outbox_messages`) are saved in **one
transaction**, and the shared outbox relay publishes the outcome.

## Endpoints

| Method | Path | Auth |
|---|---|---|
| GET | `/api/payments/orders/{orderId}` | owner / Admin (also used by orders' details composition) |
| GET | `/api/payments?status=` | Admin |
| GET | `/api/payments/provider` | - (`mock` or `stripe`) |
| GET | `/health`, `/health/ready` | - |

## Kafka

- Consumes (group `payments`): `inventory.stock-reserved` and `inventory.stock-reserved.retry`
- Produces (outbox): `payments.payment-succeeded`, `payments.payment-failed`

## Run

```bash
dotnet run --project src/services/payments/ShopStream.Payments   # http://localhost:5004
# with Stripe test mode:
Stripe__SecretKey=sk_test_... dotnet run --project src/services/payments/ShopStream.Payments
```

In Development, payment records for Jane's seeded orders are created
([DemoPaymentSeeder.cs](ShopStream.Payments/Data/DemoPaymentSeeder.cs)).
