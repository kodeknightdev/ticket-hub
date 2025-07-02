# seed-history

Fills the analytics database with about 12 months of synthetic sales across ~30 past and upcoming
events, so the dashboard charts and forecasts have something meaningful to show on a fresh install.

What it generates:

- events spread from ~11 months ago to ~2.5 months ahead, with 45-120 day on-sale windows
- daily sales with an on-sale spike, decay, weekend lift, a pre-event surge and summer/December seasonality
- dynamic price changes every week or so, driven by pace against a target curve
- holds that convert, expire or get released, plus orders that expire unpaid
- ~2% background refunds, a refund spike on one upcoming event, and check-ins for past events
  (one recent show sits at 64% to trip the check-in alert)
- a few upcoming events that pace badly, so "at risk" shows up in the performance table

Events are owned by the demo organizers (`organizer@tickethub.local`, `organizer2@tickethub.local`)
and two other organizer ids, which makes the organizer scoping visible in the dashboard.

It writes facts straight into the analytics tables (the read side is ours to rebuild) and then runs
the rollup refresh and forecast retrain jobs. Re-running it replaces its own events and leaves
everything that came in through Kafka alone.

```bash
# against the compose stack (also available as the optional `seed-history` service)
docker compose -f deploy/docker-compose.yml --profile seed run --rm seed-history

# locally against SQLite, handy for poking at the forecasting code
cd tools/seed-history
DATABASE_URL=sqlite:///./analytics.db python seed_history.py --events 30
```
