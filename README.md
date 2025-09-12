# ticket-hub

ticket-hub is a ticketing platform for concerts and live events, built for high-demand on-sales.
Buyers pick seats on a live seat map, hold them for ten minutes and pay; organizers publish events
and watch prices react to demand; door staff scan QR tickets. A separate business dashboard gives
the owner and organizers sales, demand and attendance analytics with forecasts of where each event
is heading. It is a microservices monorepo with two frontends, each behind its own Backend-for-Frontend.

## Features

**Ticketing app**
- Event catalogue with search by city, date range and category
- Interactive seat map with live availability for every viewer
- Ten-minute seat holds (up to 8 seats) with a countdown, released automatically on expiry
- Demand-based pricing within organizer-defined bounds, locked at hold time
- Checkout with Stripe in test mode or a built-in mock payment provider
- QR-code tickets, "My tickets" and ticket detail pages
- Door scanner (manual code or camera) that refuses double check-ins
- Organizer tools: create events with sections and price tiers, publish, cancel (with automatic refunds)
- Emails for confirmations, expiring holds, refunds and cancellations

**Business dashboard**
- KPI overview with change vs the previous period and trend lines
- Sales over time and breakdowns by event, artist, category, city and venue
- Sell-through heatmap by venue section
- Event performance table with pace vs forecast (On track / At risk / Sold out)
- Event drill-down: sales curve vs forecast with a confidence band, predicted sell-out date, price changes vs sales velocity
- Platform revenue forecast for 30, 60 and 90 days, with backtested accuracy
- Purchase heatmap by weekday and hour, lead-time distribution, conversion funnel, pricing impact
- Rule-based alerts, cancellation progress, CSV export and a printable summary report
- Owners see everything; organizers only ever see their own events

## Tech stack

.NET 9 · ASP.NET Core · EF Core · SignalR · gRPC · YARP (BFFs) · Kafka · SQL Server · Redis · Stripe (test mode) · MailKit ·
Python/FastAPI/pandas/statsmodels · Angular · Angular Material · ECharts · OpenTelemetry/Jaeger · Docker · Helm/Kubernetes · Terraform (Azure) · Azure DevOps

## Getting started

Prerequisites: Docker Desktop (or Docker Engine with Compose v2.24+) and about 8 GB of free memory.
For running pieces outside Docker you also need the .NET 9 SDK, Python 3.13 and Node 22.

```bash
cp .env.example .env
docker compose up --build
```

The first start takes a few minutes while SQL Server initialises and every service migrates its database.
On macOS, port 5000 is taken by AirPlay Receiver; set `TICKETING_BFF_PORT=5050` in `.env` (the apps themselves don't need it).

| What | URL |
|---|---|
| Ticketing app | http://localhost:3000 |
| Business dashboard | http://localhost:3100 |
| Ticketing BFF / Dashboard BFF | http://localhost:5000 · http://localhost:5100 |
| API docs (Scalar) per service | http://localhost:5201/scalar … http://localhost:5205/scalar |
| Pricing / analytics API docs | http://localhost:8001/docs · http://localhost:8002/docs |
| Kafka UI | http://localhost:8085 |
| MailHog (all outgoing email) | http://localhost:8025 |
| Jaeger (traces) | http://localhost:16686 |

Payments run against the mock provider until you put real Stripe **test** keys (`sk_test_…`) in `.env`;
`docker compose --profile stripe up stripe-cli` then forwards Stripe webhooks to the orders service.

## Seed data and demo logins

In Development every service seeds itself: five venues, eight upcoming published events with seat maps
and price tiers (split between two organizers), and the demo accounts below.

To fill the dashboard with about 12 months of realistic history (30 past and upcoming events,
seasonality, refunds, check-ins, price changes) run the one-shot seeder once the stack is up:

```bash
docker compose run --rm seed-history
```

| Role | Email |
|---|---|
| Buyer | `buyer@tickethub.local` |
| Organizer | `organizer@tickethub.local` |
| Staff (door scanner) | `staff@tickethub.local` |
| Owner (dashboard) | `owner@tickethub.local` |
| Admin | `admin@tickethub.local` |

All demo accounts use the password `Demo@12345`. It comes from `SEED_DEMO_PASSWORD` in `.env.example`,
is only used by the Development seeder and is local demo data, not a real credential.
A second organizer (`organizer2@tickethub.local`) and buyer (`buyer2@tickethub.local`) exist for trying out scoping.

To simulate an on-sale rush against the running stack:

```bash
docker compose run --rm load-sim --buyers=200 --seats=2
```

## Running tests

```bash
dotnet test TicketHub.sln
```

```bash
(cd src/services/pricing && pytest) && (cd src/services/analytics && pytest) && (cd ticketing-frontend && npx ng test --watch=false --browsers=ChromeHeadless) && (cd dashboard-frontend && npx ng test --watch=false --browsers=ChromeHeadless)
```

The .NET suite includes Testcontainers tests for seating and orders; they are skipped automatically when Docker isn't available.
