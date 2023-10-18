# How it works

The main user journeys, one diagram each. Checkout is summarised here; every state and failure of
the saga is covered in [order-saga.md](order-saga.md).

## 1. Sign in and stay signed in

Identity issues a 15-minute JWT and a 7-day refresh token. The React client refreshes transparently
on a 401 ([client.ts](../react-frontend/src/api/client.ts)). Refresh tokens rotate on every use, and
replaying an old one revokes the whole family.

```mermaid
sequenceDiagram
    autonumber
    actor U as Customer
    participant W as web
    participant G as gateway
    participant I as identity
    U->>W: email + password
    W->>G: POST /api/auth/login
    G->>I: (rate limited: 10/min per IP)
    I-->>W: access token (15 min) + refresh token (7 days)
    Note over W: later, the access token expires
    W->>G: GET /api/orders (expired token)
    G-->>W: 401
    W->>I: POST /api/auth/refresh (refresh token)
    I->>I: revoke old token, issue new pair
    I-->>W: new tokens
    W->>G: retry GET /api/orders
```

## 2. Browse and add to cart

Product reads go through **cache-aside** in catalog (Redis, 10 min TTL). Adding to the cart makes
cart call catalog for the current price, through a bulkhead → retry → circuit breaker → timeout stack.

```mermaid
sequenceDiagram
    autonumber
    actor U as Customer
    participant W as web
    participant G as gateway
    participant CA as cart
    participant CT as catalog
    participant R as Redis
    participant DB as catalog_db
    U->>W: open a product
    W->>G: GET /api/products/{id}
    G->>CT: forward
    CT->>R: GET catalog:product:id:{id}
    alt cache hit
        R-->>CT: product JSON (X-Cache: HIT)
    else cache miss
        CT->>DB: SELECT product
        CT->>R: SET with TTL (X-Cache: MISS)
    end
    CT-->>W: product
    U->>W: Add to cart
    W->>G: POST /api/cart/items
    G->>CA: forward (JWT checked at the gateway and again by cart)
    CA->>CT: GET /api/products/{id} (resilience policy)
    CT-->>CA: current price
    CA->>CA: store line in Redis, TTL 7 days
    CA-->>W: cart
```

If catalog is down, the circuit opens after five consecutive failures. The cart then answers 503
immediately instead of waiting on timeouts.

## 3. Checkout - happy path (summary)

```mermaid
sequenceDiagram
    autonumber
    actor U as Customer
    participant O as orders
    participant K as Kafka
    participant I as inventory
    participant P as payments
    participant E as email
    U->>O: POST /api/orders (Idempotency-Key)
    O->>O: cart (REST) + prices (gRPC), save order + outbox row
    O-->>U: 201 Pending
    O->>K: order-placed
    K->>I: reserve stock
    I->>K: stock-reserved
    K->>P: charge (Stripe test / mock)
    P->>K: payment-succeeded (outbox)
    K->>O: Paid → Confirmed
    O->>K: order-confirmed
    K->>E: confirmation email
```

## 4. Checkout - payment declined (compensation)

```mermaid
sequenceDiagram
    autonumber
    participant O as orders
    participant K as Kafka
    participant I as inventory
    participant P as payments
    participant E as email
    O->>K: order-placed
    K->>I: reserve stock
    I->>K: stock-reserved
    K->>P: charge with pm_card_chargeDeclined
    P->>K: payment-failed
    K->>E: "We couldn't take your payment"
    K->>O: InventoryReserved → Cancelled
    O->>K: order-cancelled
    K->>I: release the reservation
    K->>E: "Your order was cancelled"
```

## 5. Order details - one call, three services

The order page calls `GET /api/orders/{id}/details`. Orders loads its own order, then calls payments
(REST) and catalog (gRPC) **in parallel** and merges the answers. If either dependency fails, the
page still renders, with a warning.

```mermaid
sequenceDiagram
    autonumber
    participant W as web
    participant O as orders
    participant P as payments
    participant C as catalog
    W->>O: GET /api/orders/{id}/details
    O->>O: load order (orders_db)
    par
        O->>P: GET /api/payments/orders/{id}
        P-->>O: payment record
    and
        O->>C: gRPC GetProductsByIds
        C-->>O: current names / prices
    end
    O-->>W: { order, payment, products, warnings }
```

## 6. A price changes

When an admin edits a price, catalog invalidates the cached product and publishes
`product-price-changed` with the **new price in the event**. Cart flags the line in every cart that
holds the product, without calling catalog back. The customer sees "now $x" in the cart, and checkout
always charges the catalog price.

```mermaid
sequenceDiagram
    autonumber
    actor A as Admin
    participant CT as catalog
    participant R as Redis
    participant K as Kafka
    participant CA as cart
    A->>CT: PUT /api/products/{id} (new price)
    CT->>CT: save
    CT->>R: DEL cached product
    CT->>K: product-price-changed {oldPrice, newPrice}
    K->>CA: event
    CA->>CA: inbox check, then mark lines priceStale + latestPrice
```

## State diagrams

### Order

```mermaid
stateDiagram-v2
    [*] --> Pending: order placed
    Pending --> InventoryReserved: stock-reserved
    Pending --> Cancelled: stock-rejected / customer cancels
    InventoryReserved --> Paid: payment-succeeded
    InventoryReserved --> Cancelled: payment-failed / reservation expired / customer cancels
    Paid --> Confirmed: immediately
    Confirmed --> [*]
    Cancelled --> [*]
```

Enforced by the transition table in [Order.cs](../src/services/orders/ShopStream.Orders/Domain/Order.cs).

### Stock reservation

```mermaid
stateDiagram-v2
    [*] --> Reserved: order-placed and enough stock
    Reserved --> Committed: payment-succeeded (on hand decreases)
    Reserved --> Released: order-cancelled
    Reserved --> Expired: 15 min without payment
    Committed --> [*]
    Released --> [*]
    Expired --> [*]
```

See [service.py](../src/services/inventory/app/service.py).

### Payment

```mermaid
stateDiagram-v2
    [*] --> Charging: stock-reserved
    Charging --> Succeeded: provider accepted
    Charging --> Failed: declined / over mock limit
    Succeeded --> [*]
    Failed --> [*]
```

There is one payment per order, enforced by a unique index on `OrderId`
([PaymentsDbContext.cs](../src/services/payments/ShopStream.Payments/Data/PaymentsDbContext.cs)).
