# Tech stack

Versions are pinned exactly (`global.json`, `Directory.Packages.props`, `package-lock.json`,
compiled `requirements.txt`, image tags, `.terraform.lock.hcl`). Anything newer than the version
listed here hasn't been evaluated.

## Runtime and frameworks

| Technology | Version | Why |
|---|---|---|
| .NET SDK | 9.0.200 (`global.json`, roll forward on patch) | Current STS with C# 13, Minimal APIs, HybridCache and `HtmlRenderer` for Razor emails |
| ASP.NET Core | 9.0.2 | Minimal APIs, built-in rate limiting, antiforgery, output of ProblemDetails |
| C# | 13 | collection expressions, primary constructors keep services small |
| EF Core (SqlServer, Design, Sqlite for tests) | 9.0.2 | migrations per service; `ExecuteUpdate` for conditional check-in and outbox leases |
| SignalR + StackExchangeRedis backplane | 9.0.2 | live seat map across several seating replicas |
| gRPC (Grpc.AspNetCore / Grpc.Net.ClientFactory) | 2.67.0 | low-latency typed calls for holds and availability |
| Grpc.Tools / Google.Protobuf | 2.67.0 / 3.29.3 | code generation from `shared/protos` |
| YARP (Yarp.ReverseProxy) | 2.3.0 | proxying CRUD and WebSockets in the BFFs with per-route auth and rate limiting |
| Microsoft.AspNetCore.OpenApi + Scalar.AspNetCore | 9.0.2 + 2.0.20 | OpenAPI documents and a browsable UI at `/scalar` |
| Microsoft.Extensions.Caching.Hybrid | 9.3.0 | L1/L2 cache for dashboard responses with stampede protection |
| Microsoft.Extensions.Http.Resilience | 9.2.0 | standard retry/timeout/circuit-breaker/bulkhead pipeline |
| Python | 3.13.3 (`python:3.13.3-slim-bookworm`) | pricing and analytics, where pandas/statsmodels are the natural tools |
| FastAPI / uvicorn | 0.115.12 / 0.34.0 | small async APIs with OpenAPI at `/docs` |
| pydantic / pydantic-settings | 2.11.1 / 2.8.1 | request models and env-based settings |
| SQLAlchemy / Alembic | 2.0.40 / 1.15.2 | ORM and migrations; dialect-specific columnstore DDL in Alembic |
| pyodbc + Microsoft ODBC Driver 18 | 5.2.0 + msodbcsql18 18.5.1.1 | SQL Server access from Python |
| pandas / numpy / scipy | 2.2.3 / 2.2.4 / 2.15.2 | rollups and time series handling |
| statsmodels | 0.14.4 | Holt-Winters / exponential smoothing forecasts |
| APScheduler | 3.11.0 | rollup refresh and hourly retrain inside the analytics process |

## Messaging and data

| Technology | Version | Why |
|---|---|---|
| Apache Kafka (KRaft) | 3.9.0 (`apache/kafka:3.9.0`) | durable, partitioned event log; no ZooKeeper |
| Confluent.Kafka (.NET) / confluent-kafka (Python) | 2.8.0 / 2.8.2 | librdkafka-based clients with idempotent producers |
| Kafka UI | provectuslabs/kafka-ui v0.7.2 | inspect topics, consumer lag and DLQs locally |
| SQL Server | 2022 CU17 (`mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04`) | transactional stores and columnstore analytics in one engine |
| Redis | 7.4.2 (`redis:7.4.2-alpine`) | seat locks with Lua, SignalR backplane, BFF sessions, L2 cache |
| StackExchange.Redis | 2.8.24 | Redis client with Lua script support |

## Libraries

| Technology | Version | Why |
|---|---|---|
| Stripe.net | 47.3.0 | PaymentIntents, refunds and webhook signature verification (test mode only) |
| QRCoder | 1.6.0 | PNG QR codes for tickets and emails, no native dependencies |
| MailKit | 4.10.0 | SMTP to MailHog with inline images |
| CsvHelper | 33.0.1 | streaming CSV exports |
| FluentValidation | 11.11.0 | request validation in identity and events |
| Serilog.AspNetCore | 9.0.0 | structured console logs with trace ids |
| OpenTelemetry (.NET) | 1.11.x | traces, metrics and logs over OTLP |
| OpenTelemetry (Python) | 1.31.1 / instrumentation 0.52b1 | traces from FastAPI and Kafka processing |
| AspNetCore.HealthChecks.SqlServer / Redis / Kafka | 9.0.0 | readiness checks |

## Frontends

| Technology | Version | Why |
|---|---|---|
| Angular | 19.2.10 (CLI 19.2.11) | standalone components, signals and the new control flow; 19 is the LTS line covering this project |
| Angular Material + CDK | 19.2.15 | tables with sort/paginator, date range picker, consistent UI |
| @microsoft/signalr | 8.0.7 | WebSocket client for the seat hub |
| @zxing/browser | 0.1.5 | camera QR scanning on the door scanner page |
| Apache ECharts / ngx-echarts | 5.6.0 / 19.0.0 | heatmaps, funnels, confidence bands |
| date-fns | 4.1.0 | date maths for filters |
| TypeScript | 5.7.3 | supported by Angular 19.2 |
| Jasmine / Karma | 5.6.0 / 6.4.4 | Angular 19's default unit test setup |
| Node (build image) | 22.14.0 | LTS for the build stage |
| nginx (serving) | `nginxinc/nginx-unprivileged:1.27.4-alpine` | non-root static hosting + BFF proxy |

## Testing

| Technology | Version | Why |
|---|---|---|
| xUnit | 2.9.3 (runner 3.0.2) | .NET tests |
| Shouldly | 4.3.0 | readable assertions |
| Microsoft.AspNetCore.Mvc.Testing | 9.0.2 | in-memory BFF and service tests with stubbed downstreams |
| Testcontainers (MsSql, Redis, Kafka) | 4.2.0 | real SQL Server and Redis for the concurrency tests |
| coverlet.collector | 6.0.4 | coverage in CI |
| pytest / httpx | 8.3.5 / 0.28.1 | Python tests and FastAPI TestClient |

## Infrastructure and delivery

| Technology | Version | Why |
|---|---|---|
| Docker / Compose | Compose spec with `include` (v2.24+) | whole stack locally with one command |
| Base images | `mcr.microsoft.com/dotnet/sdk:9.0.200`, `aspnet:9.0.2`, `runtime:9.0.2` | match the SDK pin; runtime runs as `$APP_UID` |
| Jaeger | 1.66.0 | local trace UI with OTLP ingest |
| MailHog | v1.0.1 | captures every email locally |
| Stripe CLI | v1.25.0 (optional) | forwards test-mode webhooks to orders |
| Helm | 3.17.3 | umbrella chart + library chart |
| Kubernetes | AKS (version chosen by Azure default channel) | HPA, PDB, workload identity |
| Terraform (optional) | 1.11.4 (`required_version >= 1.11.0, < 1.14.0`) | one way to create the Azure resources; the app and chart don't depend on it |
| azurerm provider | 4.37.0 (locked) | AKS, ACR, Azure SQL, Redis, Key Vault, identities |
| Secrets Store CSI driver, Azure provider | AKS add-on (`key_vault_secrets_provider`) | mounts Key Vault secrets as files when `secrets.provider=keyvault` |
| Azure Event Hubs (Kafka endpoint) | optional, `MESSAGING__PROVIDER=eventhubs` | managed alternative to running Kafka on Azure; same Confluent clients over SASL_SSL |
| Azure DevOps Pipelines | YAML, `ubuntu-24.04` agents | build, scan, images, plan, deploy with approvals |
