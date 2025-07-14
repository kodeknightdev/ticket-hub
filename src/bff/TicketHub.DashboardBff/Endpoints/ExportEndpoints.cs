using System.Globalization;
using System.Text.Json;
using CsvHelper;
using TicketHub.BffKit;
using TicketHub.DashboardBff.Clients;
using TicketHub.DashboardBff.Scoping;

namespace TicketHub.DashboardBff.Endpoints;

public static class ExportEndpoints
{
    private static readonly Dictionary<string, (string Path, string? RowsProperty, (string, string?)[] Extra)> Reports = new()
    {
        ["performance"] = ("/analytics/events/performance", "rows", []),
        ["sales"] = ("/analytics/sales/timeseries", "points", []),
        ["breakdown-event"] = ("/analytics/breakdowns", "rows", [("by", "event"), ("limit", "50")]),
        ["breakdown-artist"] = ("/analytics/breakdowns", "rows", [("by", "artist"), ("limit", "50")]),
        ["breakdown-category"] = ("/analytics/breakdowns", "rows", [("by", "category"), ("limit", "50")]),
        ["breakdown-city"] = ("/analytics/breakdowns", "rows", [("by", "city"), ("limit", "50")]),
        ["breakdown-venue"] = ("/analytics/breakdowns", "rows", [("by", "venue"), ("limit", "50")]),
        ["pricing-impact"] = ("/analytics/pricing-impact", "rows", []),
        ["alerts"] = ("/analytics/alerts", null, []),
        ["lead-time"] = ("/analytics/demand/lead-time", "buckets", []),
        ["funnel"] = ("/analytics/funnel", "stages", [])
    };

    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/bff/export/{report}.csv", async (string report, HttpContext ctx, ScopeResolver scopes, AnalyticsApi analytics) =>
        {
            if (!Reports.TryGetValue(report, out var spec)) return Results.NotFound();

            var scope = await scopes.ResolveAsync(ctx, ctx.RequestAborted);
            var query = DashboardQuery.FromRequest(ctx.Request);
            var data = await analytics.GetAsync(spec.Path, query.ToQuery(scope, spec.Extra), ctx.RequestAborted);
            var rows = spec.RowsProperty is null ? data : data.GetProperty(spec.RowsProperty);

            ctx.Response.ContentType = "text/csv; charset=utf-8";
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"tickethub-{report}-{DateTime.UtcNow:yyyyMMdd}.csv\"";
            await using var writer = new StreamWriter(ctx.Response.Body);
            await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            await WriteRows(csv, rows);
            return Results.Empty;
        })
        .RequireAuthorization(BffSession.UserPolicy)
        .RequireRateLimiting("exports")
        .WithTags("Export");

        return app;
    }

    internal static async Task WriteRows(CsvWriter csv, JsonElement rows)
    {
        var list = rows.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).ToList();
        if (list.Count == 0) return;

        var columns = list[0].EnumerateObject().Where(p => p.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)).Select(p => p.Name).ToList();
        foreach (var column in columns) csv.WriteField(column);
        await csv.NextRecordAsync();

        foreach (var row in list)
        {
            foreach (var column in columns)
            {
                csv.WriteField(row.TryGetProperty(column, out var value) ? value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => "",
                    _ => value.GetRawText()
                } : "");
            }
            await csv.NextRecordAsync();
        }
    }
}
