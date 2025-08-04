# dashboard-frontend

Angular 19 analytics app for the business owner and organizers, with Apache ECharts via
`ngx-echarts`. It talks only to `dashboard-bff`; organizers automatically see only their own events.

| Route | What |
|---|---|
| `/` | KPI cards with change vs previous period and trend sparklines, sales chart, top alerts |
| `/sales` | revenue and tickets over time, top events/artists/categories/cities/venues, section sell-through heatmap |
| `/events` | sortable, paginated performance table with pace vs forecast and CSV export |
| `/events/:id` | cumulative sales vs forecast with 80% band, predicted sell-out, velocity vs price changes, section fill |
| `/forecast` | platform revenue forecast for 30/60/90 days and backtest MAPE |
| `/demand` | weekday × hour purchase heatmap, lead-time distribution |
| `/funnel` | held → ordered → paid → checked in, hold expiry, pricing impact |
| `/alerts` | rule-based alerts and cancellation saga progress |
| `/report` | printable summary of the current filters |

The filter bar (date range, granularity, city, category, venue, and organizer for owners) drives every page.

```bash
npm ci
npm start                                                   # proxies /bff and /api to :5100
npx ng test --watch=false --browsers=ChromeHeadless
npx ng build
```
