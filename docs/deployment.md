# Deployment

There are three layers, all kept in the repo:
- **Docker Compose** for local runs;
- **Kubernetes manifests** (Kustomize) for AKS;
- **Terraform** for the Azure resources underneath.

The Azure DevOps pipeline ties them together. Nothing here touches a real cloud account by default:
every id, key and hostname is a placeholder.

## Docker Compose

[`deploy/docker-compose.yml`](../deploy/docker-compose.yml) runs the whole system:

| Group | Containers |
|---|---|
| Services | identity, catalog, orders, payments, inventory, cart, email, gateway, web |
| Messaging | kafka (Bitnami 3.5.1, KRaft, no ZooKeeper), kafka-init (creates every topic plus its `.retry` and `.dlq`), kafka-ui |
| Data | postgres (five databases via [init.sql](../deploy/postgres/init.sql)), redis |
| Mail | mailhog |
| Log aggregation | loki, promtail ([promtail.yml](../deploy/observability/promtail.yml) tails every container through the Docker socket), grafana (Loki pre-provisioned via [grafana-datasources.yml](../deploy/observability/grafana-datasources.yml)) |

Every service has a healthcheck, and dependencies use `condition: service_healthy` (or
`service_completed_successfully` for kafka-init), so services start in order. The compose default is
`ASPNETCORE_ENVIRONMENT=Development`: migrations run and the demo data is seeded.

```bash
cp .env.example .env
docker compose -f deploy/docker-compose.yml --env-file .env up --build    # app on :3000, gateway on :5000
cd deploy && docker compose up --build                                    # + override: every service on its own host port
```

[`docker-compose.override.yml`](../deploy/docker-compose.override.yml) publishes Postgres, Redis and
every service directly (5001-5004, 8001, 3001, 3002, gRPC 5012), so you can run one service from an
IDE against the rest.

**Logs.** Open Grafana at http://localhost:3030 → Explore → Loki. Example queries:
- `{service="orders", level=~"(?i)error"}` - errors from one service (Promtail turns `@l` into the `level` label; .NET leaves it out for Information);
- `{service=~".+"} | json | CorrelationId="<id>"` - one request across every service. The id is in each response's `X-Correlation-ID` header.

**Validate:**

```bash
docker compose -f deploy/docker-compose.yml config -q
```

## Images

Multi-stage Dockerfiles for .NET (SDK 7.0.401 builds, ASP.NET 7.0.11 runs as a non-root user), slim
images for Python (`python:3.11.5-slim`) and Node (`node:18.17.1-alpine3.18`), and the React build
served by `nginx:1.25.2-alpine`, which also proxies `/api` to the gateway. All base images are pinned
to exact tags.

## Kubernetes

[`deploy/k8s`](../deploy/k8s) uses plain Kustomize - no Helm needed to render it.

- **base/** - for every service:
  - a Deployment (liveness `/health`, readiness `/health/ready`, requests and limits, non-root) and a Service;
  - a shared ConfigMap and a Secret **template** with placeholder values;
  - an NGINX Ingress (`/api`, `/swagger`, `/docs` → gateway, `/` → web);
  - `autoscaling/v2` HPAs for catalog and orders.
- **overlays/dev** - namespace `shop-stream-dev`, `Development` settings, HPAs 1-2, and in-cluster StatefulSets for Postgres, Redis and Kafka (KRaft), plus MailHog and a topic-creation Job.
- **overlays/prod** - namespace `shop-stream`:
  - 2-3 replicas per service, an orders HPA of 3-12, larger resources for orders, catalog and gateway;
  - PodDisruptionBudgets and a TLS Ingress;
  - managed Postgres and Redis hostnames (from Terraform outputs) in the ConfigMap.

APIs used: `apps/v1`, `networking.k8s.io/v1`, `autoscaling/v2`, `policy/v1`, `batch/v1`. These match
AKS 1.25-1.27. Logs go to stdout as JSON; on AKS, Container Insights ships them to Log Analytics.

Create the real secret before the first deploy. It is never committed:

```bash
kubectl -n shop-stream-dev create secret generic shop-stream-secrets \
  --from-literal=jwt-signing-key=... --from-literal=postgres-user=shopstream \
  --from-literal=postgres-password=... --from-literal=stripe-secret-key= \
  --from-literal=seed-demo-password=Demo@12345   # dev only; omit in prod
```

**Validate:**

```bash
kubectl kustomize deploy/k8s/overlays/dev
kubectl kustomize deploy/k8s/overlays/prod
```

## Terraform

[`deploy/terraform`](../deploy/terraform) (Terraform 1.5.7, azurerm 3.75.0, lockfile committed)
describes the Azure side:
- a resource group and ACR;
- AKS with AcrPull and Container Insights into a Log Analytics workspace;
- PostgreSQL Flexible Server with one database per service ([main.tf](../deploy/terraform/main.tf));
- Azure Cache for Redis.

Subscription and tenant ids default to `00000000-0000-0000-0000-000000000000`. The Postgres admin
password has no default and comes from `TF_VAR_postgres_admin_password`. Remote state is sketched
(commented out) in [versions.tf](../deploy/terraform/versions.tf).

Outputs map onto the rest of the deployment:
- `acr_login_server` → `acrLoginServer` in the variable group;
- `postgres_host` / `redis_host` → the prod overlay ConfigMap.

**Validate (no Azure login needed, nothing is applied):**

```bash
cd deploy/terraform
docker run --rm -v "$PWD":/tf -w /tf hashicorp/terraform:1.5.7 fmt -check
docker run --rm -v "$PWD":/tf -w /tf hashicorp/terraform:1.5.7 init -backend=false
docker run --rm -v "$PWD":/tf -w /tf hashicorp/terraform:1.5.7 validate
```

## Azure DevOps pipeline

[`azure-pipelines.yml`](../azure-pipelines.yml) is multi-stage. Settings come from the variable group
**`shop-stream-common`** (placeholders such as `acrLoginServer`). Trigger path filters cover `src/`,
`react-frontend/`, `shared/`, `tests/`, `deploy/` and the root build files; Markdown-only changes are
excluded.

1. **Build & Test**
   - `DotNetCoreCLI@2` restore, build and test with coverlet coverage → `PublishTestResults@2` and `PublishCodeCoverageResults@1`.
   - Jest for cart and email, pytest for inventory, and Vitest + `vite build` for the web app, each publishing JUnit results.
   - `terraform fmt`, `init -backend=false` and `validate` in the official container.
2. **Images**
   - A detection job diffs the commit per service path (`shared/` counts for the .NET services). Only changed services are built and pushed with `Docker@2` through the **`acr-connection`** service connection; manual runs build everything.
   - Images are tagged with the build id.
3. **Deploy Dev** - `KubernetesManifest@0` bakes `overlays/dev` with Kustomize and deploys to AKS through **`aks-dev-connection`**, substituting the new image tags.
4. **Deploy Prod** - `main` only. It runs against the `shop-stream-prod` environment, which carries the manual approval check, then does a 25 % canary and a promote through `aks-prod-connection`.

**Validate locally:** the YAML must parse, and every path it references must exist:

```bash
ruby -ryaml -e 'YAML.load_file("azure-pipelines.yml"); puts "ok"'
```
