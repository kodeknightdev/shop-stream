# Development

## Project structure

```
shop-stream/
├── src/
│   ├── gateway/ShopStream.Gateway/        # YARP: routes in appsettings.json, JWT, rate limiting, Swagger UI
│   └── services/
│       ├── identity/ShopStream.Identity/  # .NET - users, JWT + refresh tokens, demo-account seeder
│       ├── catalog/ShopStream.Catalog/    # .NET - products, gRPC server (Grpc/), Redis cache-aside (Caching/)
│       ├── orders/ShopStream.Orders/      # .NET - Domain/ state machine, Application/ saga + composition,
│       │                                  #        Clients/ gRPC + REST with Polly, Data/ migrations + demo history
│       ├── payments/ShopStream.Payments/  # .NET - Providers/ anti-corruption layer (Stripe / mock), outbox
│       ├── inventory/                     # Python - app/ (FastAPI, SQLAlchemy, Kafka loop), tests/
│       ├── cart/                          # Node - src/ (Express, Redis, cockatiel), tests/
│       └── email/                         # Node - src/ (NestJS modules), templates/ (Handlebars), test/
├── react-frontend/                        # React + Vite + Tailwind; own package.json; src/pages, src/api, src/__tests__
├── shared/
│   ├── ShopStream.Contracts/              # event records, topic names, roles, demo account ids
│   ├── ShopStream.BuildingBlocks/         # logging, correlation id, ProblemDetails, JWT, health,
│   │                                      # Kafka consumer base + retry routing, Messaging/ (outbox + inbox)
│   └── protos/catalog.proto               # the only gRPC contract
├── tests/                                 # .NET tests (one project per service + TestSupport)
├── deploy/
│   ├── docker-compose.yml / .override.yml # local stack; override adds host ports for every service
│   ├── postgres/  kafka/  observability/  # init SQL, topic script, Promtail + Grafana config
│   ├── k8s/base  k8s/overlays/{dev,prod}  # Kustomize
│   └── terraform/                         # Azure infrastructure (validated, never applied from here)
├── docs/                                  # these documents
├── azure-pipelines.yml
└── ShopStream.sln  global.json  Directory.Build.props  .editorconfig  .env.example
```

## Running services without Docker

Start just the infrastructure, then run the service you are working on from your IDE or terminal:

```bash
cd deploy
docker compose up postgres redis kafka kafka-init mailhog
```

The dev override publishes Postgres on 5432, Redis on 6379 and Kafka on 9094. Each service's
Development settings point at `localhost`.

| Service | Command | Port |
|---|---|---|
| identity | `SEED_DEMO_PASSWORD=Demo@12345 dotnet run --project src/services/identity/ShopStream.Identity` | 5001 |
| catalog | `dotnet run --project src/services/catalog/ShopStream.Catalog` | 5002 (+ gRPC 5012) |
| orders | `dotnet run --project src/services/orders/ShopStream.Orders` | 5003 |
| payments | `dotnet run --project src/services/payments/ShopStream.Payments` | 5004 |
| gateway | `dotnet run --project src/gateway/ShopStream.Gateway` | 5000 |
| inventory | `cd src/services/inventory && JWT_SIGNING_KEY=local-dev-signing-key-not-for-production-0001 uvicorn app.main:create_app --factory --reload --port 8001` | 8001 |
| cart | `cd src/services/cart && JWT_SIGNING_KEY=local-dev-signing-key-not-for-production-0001 npm run dev` | 3001 |
| email | `cd src/services/email && npm run build && JWT_SIGNING_KEY=local-dev-signing-key-not-for-production-0001 npm start` | 3002 |
| web | `cd react-frontend && npm run dev` (proxies `/api` to `VITE_GATEWAY_URL`, default :5000) | 5173 |

The .NET services read the same development signing key from `appsettings.Development.json`. Set
`Kafka__Enabled=false` (or `KAFKA_ENABLED=false`) to run a service with no broker at all; events are
then logged instead of published.

## Tests

| Area | Command | What's covered |
|---|---|---|
| .NET | `dotnet test ShopStream.sln` | Order state machine, saga handler, retry routing, Polly policies (unit); orders, catalog (incl. gRPC and cache), identity and gateway (integration, `WebApplicationFactory` + EF InMemory); payments processor, outbox and inbox |
| inventory | `cd src/services/inventory && pip install -r requirements.txt && pytest` | reservations, expiry, inbox table, retry routing, API (SQLite + fakeredis) |
| cart | `cd src/services/cart && npm ci && npm test` | API, price-change consumer, inbox, cockatiel policies |
| email | `cd src/services/email && npm ci && npm test` | templates, Redis inbox, consumer retry routing, admin API |
| web | `cd react-frontend && npm ci && npm test` | components and pages with a mocked `fetch` |

The integration tests need no running infrastructure. `tests/ShopStream.TestSupport` boots a
service in memory, swaps Postgres for EF InMemory, switches Kafka off, sets a known JWT key and the
demo password, and issues test tokens.

## Conventions

- **Commits** use Conventional Commits: `feat(orders): ...`, `fix(inventory): ...`, `docs: ...`.
- **Formatting** follows `.editorconfig`: 4 spaces in C# and Python, 2 elsewhere, file-scoped namespaces, `_camelCase` private fields.
- **Errors** are always ProblemDetails. In .NET, throw an `AppException` subtype (`NotFoundException`, `ConflictException`, …) and the middleware maps it.
- **Validation** lives at the edge: FluentValidation endpoint filters (`.Validate<T>()`), pydantic models, zod, class-validator.
- **Events** are past-tense records in `ShopStream.Contracts`, wrapped in the shared envelope. Bump `schemaVersion` for breaking payload changes.
- **Messaging rules:**
  - Publish state changes through the outbox (`db.Enqueue(...)` before `SaveChanges`).
  - Every handler must be idempotent: check and record the inbox in the same transaction.
- **Configuration** comes from environment variables, never baked into images. Secrets come from `.env` or Kubernetes Secrets only.
- **Versions** are pinned exactly, including lockfiles.

## Adding a new service

1. **Create it.** For .NET, add `src/services/<name>/ShopStream.<Name>/`, reference `ShopStream.BuildingBlocks`, and start `Program.cs` with `AddServiceDefaults`, `UseServiceDefaults` and `MapShopStreamHealth`. Add the project to `ShopStream.sln`. Other languages follow the same contract: `/health`, `/health/ready`, JSON logs with `CorrelationId`, ProblemDetails.
2. **Give it its own database** in `deploy/postgres/init.sql` (and `service_databases` in Terraform). Never read another service's tables.
3. **Events:** add contracts in `shared/ShopStream.Contracts`, add the topic to `deploy/kafka/create-topics.sh` (it creates `.retry` and `.dlq` automatically), and derive consumers from `KafkaConsumerService` with an inbox.
4. **Sync calls** go through a typed client wrapped in `ResiliencePolicies`.
5. **Infrastructure:** add a Dockerfile, a compose service with a healthcheck, a gateway route and cluster, and `deploy/k8s/base/<name>/` (deployment + service) listed in `kustomization.yaml`. Add an image entry to both overlays and to the `services` parameter in `azure-pipelines.yml`.
6. **Document it:** a service `README.md`, plus rows in [architecture.md](architecture.md) and [api.md](api.md).
