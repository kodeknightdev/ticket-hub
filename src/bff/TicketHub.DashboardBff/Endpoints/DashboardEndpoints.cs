using System.Text.Json;
using System.Text.Json.Nodes;
using TicketHub.BffKit;
using TicketHub.Contracts;
using TicketHub.DashboardBff.Clients;
using TicketHub.DashboardBff.Scoping;

namespace TicketHub.DashboardBff.Endpoints;

public static class DashboardEndpoints
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Medium = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(120);

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var bff = app.MapGroup("/bff").WithTags("Dashboard").RequireAuthorization(BffSession.UserPolicy);

        bff.MapGet("/overview", (HttpContext ctx, ScopeResolver scopes, ResponseCache cache, AnalyticsApi analytics, EventsApi events) =>
            Scoped(ctx, scopes, cache, "overview", Medium, async (scope, query, ct) =>
            {
                var fanout = new Fanout();
                var kpisTask = analytics.GetAsync("/analytics/kpis", query.ToQuery(scope), ct);
                var statsTask = fanout.Optional("events", async () => (JsonElement?)await events.StatsAsync(scope.EventIds, ct));
                await Task.WhenAll(kpisTask, statsTask);
                var kpis = kpisTask.Result;
                return new Composed(new
                {
                    from = kpis.GetProperty("from").GetString(),
                    to = kpis.GetProperty("to").GetString(),
                    kpis = kpis.GetProperty("kpis"),
                    trend = kpis.GetProperty("trend"),
                    events = statsTask.Result,
                    partial = fanout.Partial,
                    failures = fanout.Failures
                }, fanout.Partial);
            }));

        bff.MapGet("/sales/timeseries", (HttpContext ctx, ScopeResolver scopes, ResponseCache cache, AnalyticsApi analytics) =>
            Scoped(ctx, scopes, cache, "timeseries", Long, async (scope, query, ct) =>
                new Composed(await analytics.GetAsync("/analytics/sales/timeseries", query.ToQuery(scope), ct), false)));

        bff.MapGet("/breakdowns", (string? by, HttpContext ctx, ScopeResolver scopes, ResponseCache cache, AnalyticsApi analytics, EventsApi events) =>
            Scoped(ctx, scopes, cache, $"breakdowns-{by ?? "event"}", Long, async (scope, query, ct) =>
            {
                var data = await analytics.GetAsync("/analytics/breakdowns", query.ToQuery(scope, ("by", by ?? "event")), ct);
                var rows = JsonNode.Parse(data.GetProperty("rows").GetRawText())!.AsArray();
                var fanout = new Fanout();
                if ((by ?? "event") == "event")
                {
                    var keys = rows.Select(r => r!["key"]!.GetValue<string>()).ToList();
                    var summaries = await fanout.Optional("events", () => events.SummariesAsync(keys, ct)) ?? [];
                    var byId = summaries.ToDictionary(s => s.Id.ToString());
                    foreach (var row in rows)
                    {
                        if (row is null || !byId.TryGetValue(row["key"]!.GetValue<string>(), out var s)) continue;
                        row["startsAt"] = s.StartsAt;
                        row["status"] = s.Status;
                        row["city"] = s.City;
                    }
                }
                return new Composed(new { by = by ?? "event", rows, partial = fanout.Partial, failures = fanout.Failures }, fanout.Partial);
            }));

        bff.MapGet("/sections/heatmap", (HttpContext ctx, ScopeResolver scopes, ResponseCache cache, AnalyticsApi analytics) =>
            Scoped(ctx, scopes, cache, "sections", Long, async (scope, query, ct) =>
                new Composed(await analytics.GetAsync("/analytics/sections/heatmap", query.ToQuery(scope), ct), false)));

        bff.MapGet("/events/performance", (HttpContext ctx, ScopeResolver scopes, ResponseCache cache, AnalyticsApi analytics, ISeatingCounts seating) =>
            Scoped(ctx, scopes, cache, "performance", Short, (scope, query, ct) => Performance(scope, query, analytics, seating, ct)));

        bff.MapGet("/events/{id:guid}/insights", async (Guid id, HttpContext ctx, ScopeResolver scopes, ResponseCache cache,
            AnalyticsApi analytics, PricingApi pricing, EventsApi events) =>
        {
            var scope = await scopes.ResolveAsync(ctx, ctx.RequestAborted);
            if (!scope.Allows(id)) return Results.NotFound();
            return await cache.GetOrComposeAsync(ctx, $"insights-{id:N}", scope, "", Medium, async ct =>
            {
                var insights = await analytics.GetOrNullAsync($"/analytics/events/{id}/insights", ct);
                if (insights is null) return new Composed(new { notFound = true }, true);
                var fanout = new Fanout();
                var historyTask = fanout.Optional("pricing", async () => (JsonElement?)await pricing.HistoryAsync(id, ct));
                var eventTask = fanout.Optional("events", async () => (await events.SummariesAsync([id.ToString()], ct)).FirstOrDefault());
                await Task.WhenAll(historyTask, eventTask);
                return new Composed(new
                {
                    insights = insights.Value,
                    priceHistory = historyTask.Result,
                    catalogue = eventTask.Result,
                    partial = fanout.Partial,
                    failures = fanout.Failures
                }, fanout.Partial);
            });
        });

        bff.MapGet("/forecast/revenue", (int? horizon, HttpContext ctx, ScopeResolver scopes, ResponseCache cache, AnalyticsApi analytics) =>
            Scoped(ctx, scopes, cache, $"revenue-{horizon ?? 30}", Long, async (scope, query, ct) =>
                new Composed(await analytics.GetAsync("/analytics/forecast/revenue", query.ToQuery(scope, ("horizon", (horizon ?? 30).ToString())), ct), false)));

        bff.MapGet("/forecast/accuracy", (HttpContext ctx, ScopeResolver scopes, ResponseCache cache, AnalyticsApi analytics) =>
            Scoped(ctx, scopes, cache, "accuracy", Long, async (_, _, ct) =>
                new Composed(await analytics.GetAsync("/analytics/forecast/accuracy", "", ct), false)));

        foreach (var (route, path) in new[]
        {
            ("/demand/heatmap", "/analytics/demand/heatmap"),
            ("/demand/lead-time", "/analytics/demand/lead-time"),
            ("/funnel", "/analytics/funnel"),
            ("/pricing-impact", "/analytics/pricing-impact"),
            ("/filters", "/analytics/filters")
        })
        {
            bff.MapGet(route, (HttpContext ctx, ScopeResolver scopes, ResponseCache cache, AnalyticsApi analytics) =>
                Scoped(ctx, scopes, cache, route.Trim('/').Replace('/', '-'), Long, async (scope, query, ct) =>
                    new Composed(await analytics.GetAsync(path, query.ToQuery(scope), ct), false)));
        }

        bff.MapGet("/alerts", (HttpContext ctx, ScopeResolver scopes, ResponseCache cache, AnalyticsApi analytics) =>
            Scoped(ctx, scopes, cache, "alerts", Short, async (scope, query, ct) =>
                new Composed(await analytics.GetAsync("/analytics/alerts", query.ToQuery(scope), ct), false)));

        bff.MapGet("/cancellations", (HttpContext ctx, ScopeResolver scopes, ResponseCache cache, OrdersApi orders) =>
            Scoped(ctx, scopes, cache, "cancellations", Short, async (scope, _, ct) =>
            {
                var all = await orders.CancellationsAsync(ct);
                var visible = all.EnumerateArray()
                    .Where(s => scope.Allows(s.GetProperty("eventId").GetGuid()))
                    .ToList();
                return new Composed(visible, false);
            }));

        return app;
    }

    internal static async Task<Composed> Performance(DashboardScope scope, DashboardQuery query, AnalyticsApi analytics, ISeatingCounts seating, CancellationToken ct)
    {
        var data = await analytics.GetAsync("/analytics/events/performance", query.ToQuery(scope), ct);
        var rows = JsonNode.Parse(data.GetProperty("rows").GetRawText())!.AsArray();
        var upcoming = rows.Where(r => r!["daysToEvent"]!.GetValue<int>() >= 0).Select(r => r!["eventId"]!.GetValue<string>()).ToList();

        var fanout = new Fanout();
        var live = upcoming.Count == 0 ? null : await fanout.Optional("seating", () => seating.SoldCountsAsync(upcoming, ct));
        foreach (var row in rows)
        {
            if (row is null) continue;
            var sold = row["sold"]!.GetValue<int>();
            // The read side lags Kafka by a few seconds; the seat map is the live truth for upcoming events.
            if (live is not null && live.TryGetValue(row["eventId"]!.GetValue<string>(), out var liveSold) && liveSold > sold)
                row["sold"] = liveSold;
            row["liveSold"] = live is not null && live.TryGetValue(row["eventId"]!.GetValue<string>(), out var l) ? l : null;
        }
        return new Composed(new { rows, partial = fanout.Partial, failures = fanout.Failures }, fanout.Partial);
    }

    internal static async Task<IResult> Scoped(HttpContext ctx, ScopeResolver scopes, ResponseCache cache, string endpoint, TimeSpan ttl,
        Func<DashboardScope, DashboardQuery, CancellationToken, Task<Composed>> compose)
    {
        var scope = await scopes.ResolveAsync(ctx, ctx.RequestAborted);
        var query = DashboardQuery.FromRequest(ctx.Request);
        try
        {
            return await cache.GetOrComposeAsync(ctx, endpoint, scope, query.ToQuery(scope), ttl, ct => compose(scope, query, ct));
        }
        catch (HttpRequestException)
        {
            return Results.Problem(statusCode: 502, title: "Analytics is unavailable");
        }
    }
}
