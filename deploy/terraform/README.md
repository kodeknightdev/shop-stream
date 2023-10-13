# terraform

Azure infrastructure for the AKS deployment: resource group, ACR, AKS (with AcrPull and Container
Insights into Log Analytics), PostgreSQL Flexible Server with one database per service, and Azure
Cache for Redis. Kafka runs in-cluster (see `deploy/k8s`).

Only placeholder ids are committed. Nothing here is applied from this repo or from CI - it is
validated only:

```bash
docker run --rm -v "$PWD":/tf -w /tf hashicorp/terraform:1.5.7 init -backend=false
docker run --rm -v "$PWD":/tf -w /tf hashicorp/terraform:1.5.7 validate
docker run --rm -v "$PWD":/tf -w /tf hashicorp/terraform:1.5.7 fmt -check
```

Outputs map onto the rest of the deployment: `acr_login_server` → the `acrLoginServer` variable in
the `shop-stream-common` group, `postgres_host` / `redis_host` → the prod overlay ConfigMap.
