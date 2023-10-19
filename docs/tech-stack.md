# Tech stack

Every version is pinned exactly - in `.csproj` files, lockfiles (`package-lock.json`,
`.terraform.lock.hcl`), `requirements.txt` and image tags. Nothing floats on `latest`.

## .NET services (identity, catalog, orders, payments, gateway)

| Technology | Version | Why |
|---|---|---|
| .NET SDK | 7.0.401 in images; `global.json` pins 7.0.101 with `rollForward: latestFeature` | Current .NET release for the project's timeframe; one SDK across all .NET services |
| ASP.NET Core runtime | 7.0.11 (`mcr.microsoft.com/dotnet/aspnet:7.0.11`) | Minimal APIs keep each service small; built-in rate limiting and ProblemDetails in .NET 7 |
| C# | 11 | Records, file-scoped namespaces, raw strings |
| EF Core / `dotnet-ef` | 7.0.2 | Migrations per service, owned types for the shipping address, raw SQL for `SKIP LOCKED` |
| Npgsql.EntityFrameworkCore.PostgreSQL | 7.0.1 | PostgreSQL provider, `jsonb` for product attributes |
| Confluent.Kafka | 2.0.2 | Official librdkafka client: idempotent producer, manual commits |
| Grpc.AspNetCore / Grpc.Net.ClientFactory / Grpc.Tools | 2.51.0 | Typed, fast price lookups from orders to catalog |
| Google.Protobuf | 3.21.12 | Message types for `shared/protos/catalog.proto` |
| Yarp.ReverseProxy | 2.0.1 | Config-driven gateway that plugs into ASP.NET auth and rate limiting |
| Polly / Microsoft.Extensions.Http.Polly | 7.2 / 7.0.2 | Retry, circuit breaker, timeout and bulkhead for HTTP and gRPC calls |
| Microsoft.Extensions.Caching.StackExchangeRedis | 7.0.2 | `IDistributedCache` over Redis for catalog's cache-aside |
| Stripe.net | 41.15.0 | PaymentIntents in test mode, request-level idempotency keys |
| Serilog.AspNetCore | 6.1.0 | Structured JSON logs (compact format) with the correlation id |
| FluentValidation | 11.4.0 | Request validation as endpoint filters → `ValidationProblem` |
| Swashbuckle.AspNetCore | 6.5.0 | OpenAPI documents and Swagger UI (also used by the gateway's aggregated UI) |
| BCrypt.Net-Next | 4.0.3 | Password hashing |
| xUnit / FluentAssertions | 2.5.0 / 6.12.0 | Unit and integration tests |
| Microsoft.AspNetCore.Mvc.Testing / EF Core InMemory | 7.0.2 | In-memory integration tests with `WebApplicationFactory` |
| coverlet.collector | 6.0.0 | Code coverage in CI |

## Python service (inventory)

| Technology | Version | Why |
|---|---|---|
| Python | 3.11.5 (`python:3.11.5-slim`) | Fast CPython, `X \| None` typing |
| FastAPI / Starlette | 0.95.1 / 0.26.1 | Async API with OpenAPI for free |
| pydantic | 1.10.7 | Request validation and settings (`BaseSettings`) |
| SQLAlchemy | 2.0.9 | Typed ORM with `with_for_update()` row locks |
| psycopg2-binary | 2.9.6 | PostgreSQL driver |
| confluent-kafka | 2.1.0 | Same librdkafka core as the .NET client |
| redis-py | 4.5.4 | Availability cache |
| PyJWT | 2.6.0 | Validates identity's tokens |
| uvicorn | 0.21.1 | ASGI server |
| pytest / httpx / fakeredis | 7.3.1 / 0.24.0 / 2.10.3 | Tests with SQLite and an in-memory Redis |

## Node services (cart, email)

| Technology | Version | Why |
|---|---|---|
| Node.js | 18.17.1 LTS (`node:18.17.1-alpine3.18`) | LTS line, built-in `fetch` and `AbortSignal.timeout` |
| Express | 4.18.2 | Small, well-known HTTP layer for the cart |
| NestJS | 10.1.0 | Structured modules / DI for the email service |
| kafkajs | 2.2.4 | Pure-JS Kafka client with manual offset commits |
| ioredis | 5.3.2 | Cart storage and the Redis inboxes |
| cockatiel | 3.1.1 | Polly-style circuit breaker, retry, timeout and bulkhead for cart → catalog |
| zod | 3.21.4 | Cart request validation |
| class-validator / class-transformer | 0.14.0 / 0.5.1 | Email DTO validation |
| nodemailer / handlebars | 6.9.4 / 4.7.7 | SMTP sending and HTML templates |
| jsonwebtoken | 9.0.0 (cart), 9.0.1 (email) | JWT validation |
| Jest / ts-jest / supertest | 29.5 (cart) and 29.6 (email) / 29.1.1 / 6.3.3 | Tests |
| ioredis-mock | 8.7.0 | Redis in tests |

## Frontend

| Technology | Version | Why |
|---|---|---|
| React | 18.2.0 | UI library |
| Vite | 4.4.9 | Fast dev server and build, `/api` proxy to the gateway |
| TypeScript | 5.1.6 | Typed API client shared with the pages |
| React Router | 6.15.0 | Nested routes for the admin area |
| TanStack Query | 4.33.0 | Server state, polling the order page while the saga runs |
| Tailwind CSS | 3.3.3 | Utility styling without a component library |
| Vitest / Testing Library | 0.34.2 / 14.0.0 | Component tests in jsdom |
| nginx | 1.25.2 (`nginx:1.25.2-alpine`) | Serves the build and proxies `/api` in the container |

## Messaging, data and infrastructure

| Technology | Version | Why |
|---|---|---|
| Apache Kafka | 3.5.1 (Bitnami image, KRaft mode - no ZooKeeper) | Durable, partitioned, replayable event log for the saga |
| Kafka UI | v0.7.1 (provectuslabs) | Inspect topics, retry and dead-letter queues |
| PostgreSQL | 15.4 (`postgres:15.4-alpine`) | One database per service |
| Redis | 7.2.1 (`redis:7.2.1-alpine`) | Carts, caches, inboxes |
| MailHog | v1.0.1 | Catches outgoing mail locally |
| Grafana Loki / Promtail / Grafana | 2.8.4 / 2.8.4 / 10.0.3 | Log aggregation across all services |
| Docker Compose | Compose spec v2 | Whole stack locally with health-gated startup |
| Kubernetes / Kustomize | AKS 1.27 APIs; Kustomize built into kubectl | `base` plus `dev` / `prod` overlays |
| Terraform / azurerm provider | 1.5.7 / 3.75.0 | ACR, AKS, Postgres Flexible Server, Redis, Log Analytics (validated, never applied from here) |
| Azure DevOps Pipelines | multi-stage YAML | Build & test, images to ACR, deploy to AKS dev and prod |
