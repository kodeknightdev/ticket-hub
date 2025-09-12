# dashboard-bff

Backend-for-Frontend for `dashboard-frontend`. Only Owner, Organizer and Admin can sign in.
Injects each organizer's own event ids into every analytics query, caches responses in HybridCache
(memory + Redis) for 30–120 s, and streams CSV exports (throttled per user).
See [docs/bff.md](../../../docs/bff.md).

## Endpoints

`/bff/login|logout|user`, `/bff/overview`, `/bff/sales/timeseries`, `/bff/breakdowns`,
`/bff/sections/heatmap`, `/bff/events/performance`, `/bff/events/{id}/insights`,
`/bff/forecast/revenue`, `/bff/forecast/accuracy`, `/bff/demand/heatmap`, `/bff/demand/lead-time`,
`/bff/funnel`, `/bff/pricing-impact`, `/bff/alerts`, `/bff/filters`, `/bff/cancellations`,
`/bff/export/{report}.csv`.

Publishes and consumes no Kafka messages.

## Running alone

```bash
export ConnectionStrings__Redis=localhost:6379
dotnet run --project src/bff/TicketHub.DashboardBff   # http://localhost:5100
```
