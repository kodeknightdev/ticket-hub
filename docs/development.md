# Development guide

## Project structure

```
ticket-hub/
├── src/
│   ├── bff/
│   │   ├── TicketHub.TicketingBff/     YARP routes, aggregation endpoints, hold rate limit, hub proxy
│   │   └── TicketHub.DashboardBff/     scoping, HybridCache response cache, CSV export
│   └── services/
│       ├── identity/                    users, JWT, refresh-token rotation, demo seeder
│       ├── events/                      events, seat maps, tiers, outbox, demo events
│       ├── seating/                     Redis holds (Lua), gRPC server, SignalR hub, expiry worker
│       ├── orders/                      orders, payments ACL, webhooks, tickets/QR, check-in, cancellation saga
│       ├── notifier/                    MailKit + Razor component email templates
│       ├── pricing/                     Python: pricing rules, price locks, history
│       └── analytics/                   Python: star schema, rollups, forecasting, alerts
├── shared/
│   ├── TicketHub.Contracts/            event payload records, topic names, roles, demo account ids
│   ├── TicketHub.BuildingBlocks/       hosting defaults, OpenTelemetry, Kafka, outbox/inbox, JWT
│   ├── TicketHub.BffKit/               cookie session, Redis token store, token refresh, CSRF, Fanout
│   └── protos/seating.proto             gRPC contract shared by seating, orders and both BFFs
├── ticketing-frontend/                  Angular 19 app for buyers, organizers and door staff
├── dashboard-frontend/                  Angular 19 + ngx-echarts analytics app
├── tests/                               .NET test projects (+ TestSupport helpers)
├── tools/
│   ├── load-sim/                        concurrent buyers competing for seats
│   └── seed-history/                    12 months of synthetic analytics data
├── deploy/
│   ├── docker-compose.yml               full local stack
│   ├── docker-compose.override.yml      publishes service ports for debugging
│   ├── helm/ticket-hub/                umbrella chart + service-lib library chart; files/ holds the
│   │                                    SQL and Kafka bootstrap scripts shared with compose
│   └── pipelines/templates/             Azure DevOps step templates
├── infra/terraform/                     modules/ + envs/dev, envs/prod
├── compose.yaml                         includes deploy/ so `docker compose up` works from the root
└── azure-pipelines.yml
```

## Running services without Docker

You still need the infrastructure. The easiest way is to start just that part of compose:

```bash
cp .env.example .env
docker compose up -d sqlserver db-init kafka kafka-init redis mailhog jaeger
```

Kafka is reachable from the host on `localhost:29092`, SQL Server on `localhost:1433`.

### .NET services

Each service has a `launchSettings.json` profile with its local port (identity 5201, events 5202,
seating 5203 + gRPC 5213, orders 5204, notifier 5205, BFFs 5000/5100) and `ASPNETCORE_ENVIRONMENT=Development`,
which turns on auto-migration and seeding. Point them at the containers with environment variables:

```bash
export Kafka__BootstrapServers=localhost:29092
export ConnectionStrings__Default="Server=localhost,1433;Database=TicketHub_Orders;User Id=orders_svc;Password=LocalDev_Orders_Pw1!;TrustServerCertificate=True"
dotnet run --project src/services/orders/TicketHub.Orders
```

`appsettings.Development.json` contains the dev-only JWT signing key all services share. Identity
also needs `SEED_DEMO_PASSWORD` (set in its launch profile).

### Python services

```bash
cd src/services/analytics
python3.13 -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
export KAFKA_BOOTSTRAP_SERVERS=localhost:29092
export DATABASE_URL="mssql+pyodbc://analytics_svc:LocalDev_Analytics_Pw1!@localhost:1433/TicketHub_Analytics?driver=ODBC+Driver+18+for+SQL+Server&TrustServerCertificate=yes"
uvicorn app.main:app --reload --port 8002
```

Without `DATABASE_URL` the services use a local SQLite file and create the schema directly, which is
enough for poking at endpoints. Set `KAFKA_ENABLED=false` to run without Kafka.
Passwords containing `@`, `:` or `/` must be URL-encoded in `DATABASE_URL`.

### Frontends

```bash
cd ticketing-frontend && npm ci && npm start     # http://localhost:4200, proxies /bff /api /hubs to :5000
cd dashboard-frontend && npm ci && npm start     # proxies /bff /api to :5100
```

`proxy.conf.json` keeps everything same-origin, so the BFF session cookie works exactly as it does behind nginx.

## Tests

| Where | What | Command |
|---|---|---|
| `tests/TicketHub.Identity.Tests` | login, refresh rotation and reuse detection, registration | `dotnet test tests/TicketHub.Identity.Tests` |
| `tests/TicketHub.Events.Tests` | create/publish through the outbox, validation, ownership, search | |
| `tests/TicketHub.Seating.Tests` | **Testcontainers** Redis + SQL Server: holds, conflicts, expiry, confirm, parallel buyers, no double sale | |
| `tests/TicketHub.Orders.Tests` | QR signing, Stripe and mock webhook signatures, order flow and compensations, check-in, cancellation saga; **Testcontainers** SQL Server for idempotency and racing scanners | |
| `tests/TicketHub.Notifier.Tests` | Razor templates, send-once | |
| `tests/TicketHub.Bff.Tests` | both BFFs with stubbed downstreams (see [bff.md](bff.md)) | |
| `src/services/pricing/tests` | pricing rules, locks at hold time, handlers, API | `pytest` |
| `src/services/analytics/tests` | ingestion, rollups, forecasting + backtest MAPE, alerts, API scoping | `pytest` |
| `*-frontend/src/**/*.spec.ts` | services with `HttpTestingController`, components, chart builders | `npx ng test --watch=false --browsers=ChromeHeadless` |

Testcontainers tests use `[DockerFact]`, which skips them when `docker info` fails (or when
`SKIP_DOCKER_TESTS=1`). Fast tests use SQLite; `ModelBuilder.ForSqliteTests` converts
`DateTimeOffset` columns and drops rowversion tokens only when the provider is SQLite.

## Conventions

- Minimal APIs grouped per area (`MapOrderEndpoints`, …); no controllers.
- One `Program.cs` per service that reads top to bottom: defaults, auth, messaging, data, services, endpoints.
- Every Kafka payload is a record in `TicketHub.Contracts`; topics are constants in `Topics`.
- Writes that need to emit an event use `db.Enqueue(...)` in the same `SaveChanges`.
- Consumers check `AlreadyProcessedAsync` and call `MarkProcessed` in the same transaction.
- `TimeProvider` everywhere instead of `DateTime.UtcNow`, so tests can move the clock.
- Comments explain *why*, not *what*. Public shared APIs get a one-line summary.
- Python: type hints, pure functions for rules and forecasting, SQLAlchemy 2.0 style, naive UTC datetimes.
- Angular: standalone components, signals for state, `inject()`, new control flow, typed HTTP calls in one service per app.
- Versions are pinned centrally (`Directory.Packages.props`, `package-lock.json`, compiled `requirements.txt`).

## Adding a service

1. Create `src/services/<name>/TicketHub.<Name>` with `Microsoft.NET.Sdk.Web`, reference `TicketHub.BuildingBlocks`.
2. In `Program.cs`: `AddServiceDefaults("<name>")`, `AddJwtAuth()` if it has protected endpoints,
   `AddKafka()` and `AddKafkaConsumer("<name>", c => c.On<Handler>(Topics.X))` if it consumes events,
   a `DbContext` that calls `model.AddOutbox().AddInbox()` and `ForSqliteTests(Database)`, then
   `MapDefaultEndpoints()` and `MigrateAndSeedAsync<TContext>()`.
3. Add payload records and topic names to `TicketHub.Contracts`, and the topics plus
   `<name>.retry`/`<name>.dlq` to `deploy/helm/ticket-hub/files/create-topics.sh`.
4. Add a database and login to `deploy/helm/ticket-hub/files/init.sql` and a password to `.env.example`.
5. Copy a Dockerfile, add the service to `deploy/docker-compose.yml`, to `services:` in the Helm
   `values.yaml`, to the image matrix and change detection in the pipeline, and to the smoke test.
6. `dotnet sln add`, add a test project under `tests/`, and a `README.md` in the service folder.
