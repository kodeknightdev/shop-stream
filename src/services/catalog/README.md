# catalog

Categories and products: search, filtering, sorting, paging, and admin CRUD. Also the **gRPC server**
other services use for authoritative product data and prices.

## REST endpoints

| Method | Path | Auth | Notes |
|---|---|---|---|
| GET | `/api/categories` | - | with active product counts |
| POST/PUT | `/api/categories[/{id}]` | Admin | |
| GET | `/api/products` | - | `search`, `category` (slug or id), `minPrice`, `maxPrice`, `sort` (`name`, `name_desc`, `price_asc`, `price_desc`, `newest`), `page`, `pageSize` (max 60) |
| GET | `/api/products/{id}` | - | detail with attributes |
| GET | `/api/products/by-slug/{slug}` | - | |
| POST | `/api/products` | Admin | |
| PUT | `/api/products/{id}` | Admin | publishes `catalog.product-price-changed` when the price changes |
| DELETE | `/api/products/{id}` | Admin | soft delete (`isActive=false`) |
| GET | `/health`, `/health/ready` | - | Postgres + Kafka |

## gRPC

`shared/protos/catalog.proto`, served on the HTTP/2-only port **8081** (5012 locally).

| RPC | Purpose |
|---|---|
| `GetProduct` | one product |
| `GetProductsByIds` | batch lookup (max 200), returns `missing_ids` as well |

Prices travel as `price_cents` (int64). Server reflection is enabled in Development, so
`grpcurl -plaintext localhost:5012 list` works.

## Kafka

- Produces: `catalog.product-price-changed` (key: productId)
- Consumes: nothing

## Data

`catalog_db`: `categories`, `products` (attributes as `jsonb`). Seeded with 6 categories and 40
products; the product ids are shared with the inventory seed.

## Run

```bash
dotnet run --project src/services/catalog/ShopStream.Catalog   # REST :5002, gRPC :5012
```
