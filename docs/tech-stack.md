# Tech stack

Every technology in the project, the exact version pinned in the repository (`global.json`,
`Directory.Packages.props`, `package-lock.json`, compiled `requirements.txt`, image tags,
`.terraform.lock.hcl`), and where it is used.

## Runtime and frameworks

| Technology | Version | Used for |
|---|---|---|
| .NET SDK | 9.0.200 (`global.json`, roll forward on patch) | building and running every .NET project: identity, events, seating, orders, notifier, both BFFs, load-sim and the test projects |
| ASP.NET Core | 9.0.2 | Minimal APIs in the services and BFFs, rate limiting in both BFFs, antiforgery and cookie auth in the BFFs, ProblemDetails everywhere |
| C# | 13 | all .NET code |
| EF Core (SqlServer, Design, Sqlite for tests) | 9.0.2 | data access and migrations in identity, events, seating, orders and notifier; SQLite for fast tests |
| SignalR + StackExchangeRedis backplane | 9.0.2 | the live seat-map hub in seating (`/hubs/seating`), fanned out across replicas through Redis |
| gRPC (Grpc.AspNetCore / Grpc.Net.ClientFactory) | 2.67.0 | the `SeatingRpc` server in seating; clients in orders, ticketing-bff and dashboard-bff |
| Grpc.Tools / Google.Protobuf | 2.67.0 / 3.29.3 | generating C# from `shared/protos/seating.proto` |
| YARP (Yarp.ReverseProxy) | 2.3.0 | pass-through routes and the SignalR WebSocket in ticketing-bff; venue/category lookups in dashboard-bff |
| Microsoft.AspNetCore.OpenApi + Scalar.AspNetCore | 9.0.2 + 2.0.20 | OpenAPI documents and the `/scalar` UI on every .NET service and BFF |
| Microsoft.Extensions.Caching.Hybrid | 9.3.0 | dashboard-bff response cache (in-process + Redis) |
| Microsoft.Extensions.Http.Resilience | 9.2.0 | retry, timeout, circuit breaker and bulkhead on every outbound HTTP and gRPC client (BFFs, orders, seating) |
| Python | 3.13.3 (`python:3.13.3-slim-bookworm`) | the pricing and analytics services and the seed-history tool |
| FastAPI / uvicorn | 0.115.12 / 0.34.0 | HTTP APIs of pricing and analytics |
| pydantic / pydantic-settings | 2.11.1 / 2.8.1 | request/response models and environment settings in pricing and analytics |
| SQLAlchemy / Alembic | 2.0.40 / 1.15.2 | data access and migrations in pricing and analytics (including the columnstore DDL) |
| pyodbc + Microsoft ODBC Driver 18 | 5.2.0 + msodbcsql18 18.5.1.1 | SQL Server connections from pricing, analytics and seed-history |
| pandas / numpy / scipy | 2.2.3 / 2.2.4 / 1.15.2 | rollups, dashboard queries and time series preparation in analytics |
| statsmodels | 0.14.4 | Holt-Winters sales and revenue forecasts in analytics |
| APScheduler | 3.11.0 | the rollup refresh and hourly forecast retrain inside analytics |

## Messaging and data

| Technology | Version | Used for |
|---|---|---|
| Apache Kafka (KRaft) | 3.9.0 (`apache/kafka:3.9.0`) | every integration event between services, plus the retry and dead-letter topics |
| Confluent.Kafka (.NET) / confluent-kafka (Python) | 2.8.0 / 2.8.2 | producers and consumer groups in all services |
| Kafka UI | provectuslabs/kafka-ui v0.7.2 | browsing topics, consumer lag and DLQs in the compose stack |
| SQL Server | 2022 CU17 (`mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04`) | one database per service (identity, events, seating, orders, notifier, pricing, analytics) |
| Redis | 7.4.2 (`redis:7.4.2-alpine`) | seat holds and sold sets in seating, the SignalR backplane, BFF sessions and tokens, the dashboard L2 cache |
| StackExchange.Redis | 2.8.24 | Lua seat-hold scripts and layout cache in seating; token store in both BFFs |

## Libraries

| Technology | Version | Used for |
|---|---|---|
| Stripe.net | 47.3.0 | PaymentIntents, refunds and webhook verification in orders (test mode only) |
| QRCoder | 1.6.0 | ticket QR codes in orders and the inline QR images in notifier emails |
| MailKit | 4.10.0 | sending email from notifier (MailHog locally) |
| CsvHelper | 33.0.1 | CSV exports in dashboard-bff |
| FluentValidation | 11.11.0 | request validation in identity and events |
| Serilog.AspNetCore | 9.0.0 | structured logs in every .NET service and BFF |
| OpenTelemetry (.NET) | 1.11.x | traces, metrics and logs from every .NET service and BFF, including Kafka spans |
| OpenTelemetry (Python) | 1.31.1 / instrumentation 0.52b1 | traces from pricing and analytics (FastAPI and Kafka processing) |
| AspNetCore.HealthChecks.SqlServer / Redis / Kafka | 9.0.0 | `/health` readiness checks in the .NET services and BFFs |

## Frontends

| Technology | Version | Used for |
|---|---|---|
| Angular | 19.2.10 (CLI 19.2.11) | ticketing-frontend and dashboard-frontend (standalone components, signals, new control flow) |
| Angular Material + CDK | 19.2.15 | UI components in both apps; `MatTable`/`MatSort`/`MatPaginator` and the date-range picker in the dashboard |
| @microsoft/signalr | 8.0.7 | live seat updates on the event page of ticketing-frontend |
| @zxing/browser | 0.1.5 | camera scanning on the door scanner page of ticketing-frontend |
| Apache ECharts / ngx-echarts | 5.6.0 / 19.0.0 | all charts in dashboard-frontend (trends, heatmaps, funnel, forecast bands) |
| date-fns | 4.1.0 | date handling in the dashboard filter bar |
| TypeScript | 5.7.3 | both frontends |
| Jasmine / Karma | 5.6.0 / 6.4.4 | unit and component tests of both frontends |
| Node (build image) | 22.14.0 | building both frontends in Docker and CI |
| nginx (serving) | `nginxinc/nginx-unprivileged:1.27.4-alpine` | serving both frontends and proxying `/bff`, `/api` and `/hubs` to their BFF |

## Testing

| Technology | Version | Used for |
|---|---|---|
| xUnit | 2.9.3 (runner 3.0.2) | all .NET test projects |
| Shouldly | 4.3.0 | assertions in the .NET tests |
| Microsoft.AspNetCore.Mvc.Testing | 9.0.2 | in-memory tests of identity, events and both BFFs with stubbed downstreams |
| Testcontainers (MsSql, Redis) | 4.2.0 | seating and orders integration tests, including the no-double-sale test |
| coverlet.collector | 6.0.4 | .NET coverage in CI |
| pytest / httpx | 8.3.5 / 0.28.1 | pricing and analytics tests, including the FastAPI test client |

## Infrastructure and delivery

| Technology | Version | Used for |
|---|---|---|
| Docker / Compose | Compose spec with `include` (v2.24+) | the full local stack (`compose.yaml` → `deploy/docker-compose.yml`) |
| Base images | `mcr.microsoft.com/dotnet/sdk:9.0.200`, `aspnet:9.0.2`, `runtime:9.0.2` | building and running every .NET image; load-sim uses the runtime image |
| Jaeger | 1.66.0 | trace UI in the compose stack |
| MailHog | v1.0.1 | capturing notifier email locally |
| Stripe CLI | v1.25.0 (optional) | forwarding test-mode webhooks to orders in compose |
| Helm | 3.17.3 | the `deploy/helm/ticket-hub` umbrella chart and its service library chart |
| Kubernetes | AKS (version chosen by Azure default channel) | running the chart with HPAs, PDBs and workload identity |
| Terraform (optional) | 1.11.4 (`required_version >= 1.11.0, < 1.14.0`) | `infra/terraform` modules and the dev/prod environments |
| azurerm provider | 4.37.0 (locked) | AKS, ACR, Azure SQL, Redis, Key Vault and identities in the Terraform modules |
| Secrets Store CSI driver, Azure provider | AKS add-on (`key_vault_secrets_provider`) | mounting Key Vault secrets into pods when `secrets.provider=keyvault` |
| Azure Event Hubs (Kafka endpoint) | optional, `MESSAGING__PROVIDER=eventhubs` | the managed Kafka-compatible broker option on Azure |
| Azure DevOps Pipelines | YAML, `ubuntu-24.04` agents | `azure-pipelines.yml`: build, scan, images, optional infra plan, deploys and smoke test |
