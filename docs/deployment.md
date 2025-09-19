# Deployment

Three layers, each usable on its own: Docker Compose for a laptop, a Helm chart for Kubernetes, and
Terraform for the Azure resources the chart can use. Azure DevOps ties them together.

## With or without Terraform

Terraform is optional. It's one way to create the Azure resources; nothing in the application or the
chart depends on it. Pick whichever path fits:

| Where | How | Dependencies come from |
|---|---|---|
| Laptop | `docker compose up --build` | SQL Server, Redis, Kafka, MailHog containers |
| Any Kubernetes cluster (kind, Docker Desktop, minikube) | Helm with `values-local.yaml` | the same images running in the cluster; secrets in a Kubernetes Secret; workload identity off |
| Azure, resources created by Terraform | `infra/terraform`, then Helm with `values-dev.yaml` / `values-prod.yaml`; pipeline run with `provisionInfra=true` | Terraform outputs fed into Helm values and the variable group |
| Azure, resources that already exist (portal, Bicep, another team) | Helm with `values-dev.yaml` / `values-prod.yaml`; pipeline run with the default `provisionInfra=false` | connection strings, Key Vault name, registry and identity client id passed in as Helm values or variable-group variables |

Anything Azure-specific is picked by configuration, and the local option is always the default:

| Setting (env var) | Values | Effect |
|---|---|---|
| `MESSAGING__PROVIDER` | `kafka` (default), `eventhubs` | `eventhubs` talks to Azure Event Hubs' Kafka endpoint: SASL_SSL with `MESSAGING__EVENTHUBS__CONNECTIONSTRING`; `Kafka__BootstrapServers` / `KAFKA_BOOTSTRAP_SERVERS` become `<namespace>.servicebus.windows.net:9093` |
| `SECRETS__PROVIDER` | `environment` (default), `keyvault` | `keyvault` also reads secrets from the files the Key Vault CSI driver mounts at `SECRETS__PATH` (default `/mnt/secrets-store`), one file per setting |
| Helm `secrets.provider` | `kubernetes` (default), `keyvault` | `kubernetes` renders a Secret read through `secretKeyRef`; `keyvault` renders a `SecretProviderClass` per service and mounts it, which needs workload identity |
| Helm `localDependencies.enabled` | `false` (default), `true` | runs SQL Server, Redis, Kafka and MailHog in the cluster, with jobs that create the databases, logins and topics |
| Helm `workloadIdentity.enabled` | `true` (default), `false` | annotates the ServiceAccount and labels pods for Azure workload identity |

The code is in [`ServiceDefaults.AddSecrets`](../shared/TicketHub.BuildingBlocks/Hosting/ServiceDefaults.cs) and
[`KafkaOptions.Apply`](../shared/TicketHub.BuildingBlocks/Messaging/KafkaOptions.cs) for .NET, and
[`app/config.py`](../src/services/pricing/app/config.py) in each Python service. The application has no blob
or file storage, so there's no storage provider to switch.

## Docker Compose

[`deploy/docker-compose.yml`](../deploy/docker-compose.yml) runs the whole platform;
[`deploy/docker-compose.override.yml`](../deploy/docker-compose.override.yml) publishes every service
port for debugging. The root [`compose.yaml`](../compose.yaml) includes both and reads `.env`, so from
the repository root:

```bash
cp .env.example .env
docker compose up --build
```

Start-up order is enforced with health checks:

```mermaid
flowchart LR
    SQL[sqlserver healthy] --> DBI[db-init: databases + logins]
    KF[kafka healthy] --> KI[kafka-init: topics]
    RD[redis healthy]
    DBI & KI & RD --> SVC[identity, events, seating, orders, notifier, pricing, analytics]
    RD --> BFF[ticketing-bff, dashboard-bff]
    BFF --> FE[frontends via nginx]
```

| Container | Notes |
|---|---|
| `sqlserver` | SQL Server 2022 CU17 (amd64; runs under emulation on Apple silicon) |
| `db-init` | runs [`deploy/helm/ticket-hub/files/init.sql`](../deploy/helm/ticket-hub/files/init.sql) with `sqlcmd` variables from `.env` |
| `kafka`, `kafka-init` | KRaft single node; [`create-topics.sh`](../deploy/helm/ticket-hub/files/create-topics.sh) creates business, retry and DLQ topics |
| `kafka-ui` | http://localhost:8085 |
| `redis`, `mailhog`, `jaeger` | supporting infrastructure |
| .NET services | multi-stage images on `aspnet:9.0.2`, run as the non-root `$APP_UID`, health check on `/alive` |
| `pricing`, `analytics` | `python:3.13.3-slim-bookworm` + ODBC Driver 18, uid 10001, Alembic migrations on start |
| `ticketing-frontend`, `dashboard-frontend` | Angular production builds served by unprivileged nginx, proxying `/bff` (and `/hubs`) to their BFF |
| `seed-history` (profile `seed`) | one-shot: `docker compose run --rm seed-history` |
| `load-sim` (profile `loadsim`) | one-shot: `docker compose run --rm load-sim --buyers=200` |
| `stripe-cli` (profile `stripe`) | forwards test-mode webhooks to `orders:8080/webhooks/stripe` |

Validate without starting anything:

```bash
docker compose -f deploy/docker-compose.yml --env-file .env.example config -q
```

Check health once it's up:

```bash
for p in 5201 5202 5203 5204 5205 5000 5100; do curl -s localhost:$p/health; echo; done
curl -s localhost:8001/health; curl -s localhost:8002/health
```

## Kubernetes (Helm)

[`deploy/helm/ticket-hub`](../deploy/helm/ticket-hub) is an umbrella chart. All workloads are
described in `values.yaml` under `services:` and rendered by the library chart
[`charts/service-lib`](../deploy/helm/ticket-hub/charts/service-lib), so every service gets the same
shape:

- Deployment (non-root, no privilege escalation, rolling update with `maxUnavailable: 0`) with
  startup/liveness probes on `/alive` and readiness on `/health`
- Service (seating also exposes `grpc` on 8081 with `appProtocol: kubernetes.io/h2c`)
- ConfigMap from `global.env` + the service's `env` (values can reference the release name)
- secrets either as `secretKeyRef`s into one Secret (`ticket-hub-secrets`, `secrets.provider: kubernetes`)
  or as a Key Vault CSI mount through a per-service `SecretProviderClass` (`secrets.provider: keyvault`, used in prod)
- PodDisruptionBudget where `pdb` is set
- HorizontalPodAutoscaler on **seating**, **orders** and **ticketing-bff**

Chart-level templates add:

- two NGINX Ingresses: `app.tickethub.local` (frontend + `/bff`, `/api`, `/hubs` to the ticketing BFF,
  with long read timeouts and cookie affinity for SignalR) and `dashboard.tickethub.local`
- a ServiceAccount annotated for **Azure workload identity** (`azure.workload.identity/client-id`),
  with pods labelled `azure.workload.identity/use: "true"`
- an optional **CronJob** that retrains forecasts (`python -m app.jobs all`), enabled in prod

- with `localDependencies.enabled`, in-cluster SQL Server (StatefulSet with a volume), Redis, Kafka (KRaft)
  and MailHog, plus `db-init` and `kafka-init` Jobs that run the same scripts as docker-compose
  ([`files/init.sql`](../deploy/helm/ticket-hub/files/init.sql), [`files/create-topics.sh`](../deploy/helm/ticket-hub/files/create-topics.sh))

Outside `values-local.yaml`, Kafka is expected to exist already (for example Strimzi at
`ticket-hub-kafka-bootstrap.kafka:9092`) or to be Event Hubs via `MESSAGING__PROVIDER=eventhubs`.

### Running on a local cluster

```bash
docker compose build                      # tags images as tickethub/<service>:local
kubectl create namespace ticket-hub
helm upgrade --install tr deploy/helm/ticket-hub -n ticket-hub -f deploy/helm/ticket-hub/values-local.yaml
kubectl -n ticket-hub get pods -w        # services restart a few times until SQL Server and Kafka are ready
kubectl -n ticket-hub port-forward svc/th-ticketing-frontend 3000:80
kubectl -n ticket-hub port-forward svc/th-dashboard-frontend 3100:80
```

On kind, load the images first (`kind load docker-image tickethub/identity:local …`). With an
NGINX ingress controller installed, the two `*.tickethub.local` hosts work too once they point at it
in `/etc/hosts`. SQL Server is amd64-only and runs emulated on Apple silicon nodes.

Validate locally (no cluster needed):

```bash
helm dependency build deploy/helm/ticket-hub
helm lint deploy/helm/ticket-hub
helm template th deploy/helm/ticket-hub -f deploy/helm/ticket-hub/values-local.yaml
helm template th deploy/helm/ticket-hub -f deploy/helm/ticket-hub/values-dev.yaml
helm template th deploy/helm/ticket-hub -f deploy/helm/ticket-hub/values-prod.yaml
```

`values-local.yaml` is the no-Azure path above. `values-dev.yaml` runs single replicas with small HPAs,
no TLS and secrets from a Kubernetes Secret; `values-prod.yaml` raises replicas and HPA limits, enables
TLS via cert-manager, turns on the CronJob and reads secrets from Key Vault through the CSI driver.

## Terraform (Azure, optional)

Only needed if you want Terraform to create the Azure resources. The outputs (`acr_login_server`,
`sql_fqdn`, `redis_hostname`, `key_vault_uri`, `workload_identity_client_id`) are exactly the values
the chart and the pipeline otherwise take as inputs.

```
infra/terraform/
├── modules/
│   ├── aks/                 AKS with OIDC issuer + workload identity, Azure RBAC, Cilium, autoscaling user pool, Key Vault CSI
│   ├── acr/                 registry + AcrPull for the kubelet identity
│   ├── mssql/               logical server (Entra ID admin only), one DB per service, larger analytics DB, firewall / private endpoint
│   ├── redis/               Azure Cache for Redis, TLS only, noeviction
│   ├── keyvault/            RBAC vault, placeholder secrets for Stripe test keys and signing keys
│   ├── log-analytics/       workspace for Container Insights
│   └── federated-identity/  user-assigned identity, federated credential for the chart's ServiceAccount, Key Vault Secrets User
└── envs/
    ├── dev/                 small SKUs, public endpoints with a placeholder firewall range
    └── prod/                Standard AKS tier, Premium ACR, private SQL endpoint, bigger analytics DB
```

Naming is `th-{env}-{location_short}-{resource}` and every resource is tagged with `project`,
`environment` and `managed_by`. The backend is a placeholder `azurerm` block; the pipeline passes the
real storage account. Copy `terraform.tfvars.example` to `terraform.tfvars` (git-ignored) to plan by hand.
All ids in the examples are `00000000-0000-0000-0000-000000000000`.

Validate locally without Azure credentials:

```bash
cd infra/terraform
terraform fmt -check -recursive
cd envs/dev && terraform init -backend=false && terraform validate
cd ../prod && terraform init -backend=false && terraform validate
```

Provider versions are locked in each env's `.terraform.lock.hcl` (hashes for Linux, macOS and Windows).

## Azure DevOps pipeline

[`azure-pipelines.yml`](../azure-pipelines.yml) with templates in
[`deploy/pipelines/templates`](../deploy/pipelines/templates):

```mermaid
flowchart LR
    A[Build & Test] --> B[Security scan]
    A -. provisionInfra=true .-> D[Infra plan]
    B --> C[Images]
    C --> E[Deploy Dev]
    D --> E
    E --> F[Smoke test]
    F --> G[Deploy Prod<br/>approval on environment]
```

| Stage | What happens |
|---|---|
| Build & Test | [`detect-changes.yml`](../deploy/pipelines/templates/detect-changes.yml) diffs the commit and sets a flag per component; only changed parts build. .NET (`--locked-mode` restore, tests with coverage, Testcontainers on the hosted agent's Docker), pytest per Python service, `ng build` + `ng test` per frontend. Test results and Cobertura coverage are published. |
| Security scan | `dotnet list package --vulnerable`, `npm audit`, `pip-audit`; reports are published as an artifact and never fail the build |
| Images | `Docker@2` build-and-push to ACR via the `acr-connection` service connection, tagged with build id and commit; main branch only |
| Infra plan | Only when the run parameter `provisionInfra` is `true` (default `false`). `TerraformTaskV4@4` init/validate/plan for dev and prod via `azure-arm-connection`; plans are published as artifacts. Nothing is applied by the pipeline. When the stage is left out, Deploy Dev depends on Images alone. |
| Deploy Dev | `HelmDeploy@0` `upgrade --install --atomic` into the `ticket-hub` namespace, environment `ticket-hub-dev` |
| Smoke test | runs a curl pod in the cluster and checks `/health` on every service and both BFFs |
| Deploy Prod | same as dev with `values-prod.yaml`, environment `ticket-hub-prod`, which carries the manual approval check |

Variables come from the `ticket-hub-common` variable group. The ones marked *Terraform only* are
needed just for `provisionInfra=true`:

| Variable | Example (placeholder) |
|---|---|
| `acrLoginServer` | `thprodacr.azurecr.io` |
| `aksResourceGroupDev` / `aksClusterDev` | `th-dev-uks-rg` / `th-dev-uks-aks` |
| `aksResourceGroupProd` / `aksClusterProd` | `th-prod-uks-rg` / `th-prod-uks-aks` |
| `tfStateResourceGroup` / `tfStateStorageAccount` (*Terraform only*) | `th-tfstate-rg` / `thtfstate` |
| `subscriptionId`, `tenantId`, `deployerObjectId` (*Terraform only*) | `00000000-0000-0000-0000-000000000000` |
| `sqlAdminGroupName`, `sqlAdminGroupObjectId` (*Terraform only*) | Entra group for SQL admins |
| `workloadClientIddev`, `workloadClientIdprod` | the workload identity's client id (Terraform output `workload_identity_client_id`, or an existing identity) |

Locally you can sanity-check the YAML with any YAML parser; every template path referenced from the
pipeline exists under `deploy/pipelines/templates`. With `provisionInfra=false` the expanded pipeline
has six stages (no Infra plan) and Deploy Dev depends only on Images.
