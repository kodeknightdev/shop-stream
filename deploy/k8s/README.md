# Kubernetes manifests

```
k8s/
├── base/                 # every service: Deployment + Service; ConfigMap, Secret template, Ingress, HPAs
└── overlays/
    ├── dev/              # namespace shop-stream-dev, 1 replica, in-cluster Postgres/Redis/Kafka/MailHog
    └── prod/             # namespace shop-stream, more replicas, bigger resources, PDBs, TLS ingress
```

Render:

```bash
kubectl kustomize deploy/k8s/overlays/dev
kubectl kustomize deploy/k8s/overlays/prod
```

Before applying to a cluster, create the real secret (the base only contains a template with
placeholders):

```bash
kubectl -n shop-stream-dev create secret generic shop-stream-secrets \
  --from-literal=jwt-signing-key=... \
  --from-literal=postgres-user=shopstream \
  --from-literal=postgres-password=... \
  --from-literal=stripe-secret-key=
```

Targets AKS 1.25-1.27 APIs (`apps/v1`, `networking.k8s.io/v1`, `autoscaling/v2`, `policy/v1`) and
the NGINX ingress controller (`ingressClassName: nginx`). Prod expects managed Postgres/Redis and an
existing Kafka; the hostnames in `overlays/prod/kustomization.yaml` are placeholders.
