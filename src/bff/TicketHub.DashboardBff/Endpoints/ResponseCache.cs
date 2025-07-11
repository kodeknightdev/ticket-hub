using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using TicketHub.Contracts;
using TicketHub.DashboardBff.Scoping;

namespace TicketHub.DashboardBff.Endpoints;

public sealed record Composed(object Body, bool Partial);

/// <summary>
/// Cache-aside over HybridCache (in-process L1, Redis L2). Keys include the user's scope so an
/// organizer can never be served another organizer's cached numbers. Partial responses are not cached.
/// </summary>
public sealed class ResponseCache(HybridCache cache)
{
    private static readonly HybridCacheEntryOptions ReadOnly = new()
    {
        Flags = HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite | HybridCacheEntryFlags.DisableUnderlyingData
    };

    public async Task<IResult> GetOrComposeAsync(HttpContext ctx, string endpoint, DashboardScope scope, string query, TimeSpan ttl, Func<CancellationToken, Task<Composed>> compose)
    {
        var key = $"dash:{endpoint}:{scope.CacheKey}:{query}";
        var ct = ctx.RequestAborted;

        var cached = await cache.GetOrCreateAsync<string?>(key, _ => ValueTask.FromResult<string?>(null), ReadOnly, cancellationToken: ct);
        if (cached is not null)
        {
            ctx.Response.Headers[WellKnownHeaders.Cache] = "HIT";
            return Results.Content(cached, "application/json");
        }

        var composed = await compose(ct);
        var json = JsonSerializer.Serialize(composed.Body, Json.Options);
        if (!composed.Partial)
            await cache.SetAsync(key, json, new HybridCacheEntryOptions { Expiration = ttl, LocalCacheExpiration = ttl }, cancellationToken: ct);

        ctx.Response.Headers[WellKnownHeaders.Cache] = "MISS";
        return Results.Content(json, "application/json");
    }
}
