# analytics

The read side. Consumes every business event into a star schema (dimensions, facts with clustered
columnstore indexes on SQL Server, daily rollups), refreshes rollups every five minutes, retrains
forecasts hourly (Holt-Winters with a similar-events fallback), runs a backtest and evaluates alert
rules. Fact tables get clustered columnstore indexes on SQL Server; tests run the same models on SQLite.

## Endpoints

`/analytics/kpis`, `/sales/timeseries`, `/breakdowns`, `/sections/heatmap`, `/events/performance`,
`/events/ids`, `/events/{id}/insights`, `/forecast/revenue`, `/forecast/accuracy`, `/demand/heatmap`,
`/demand/lead-time`, `/funnel`, `/pricing-impact`, `/alerts`, `/filters`, plus `POST /analytics/jobs/refresh`
and `/analytics/jobs/retrain`. All reads accept `from`, `to`, `eventIds`, `city`, `category`, `venue`, `organizerId`.

## Events

Consumes (`analytics`): all `events.*`, `seating.*` (except `hold-expiring`), `orders.*` and
`pricing.price-updated`. Publishes nothing.

## Running alone

```bash
cd src/services/analytics
pip install -r requirements.txt
KAFKA_ENABLED=false uvicorn app.main:app --reload --port 8002
python -m app.jobs all      # refresh rollups + retrain, as the Helm CronJob does
pytest
```
