using Microsoft.Extensions.Caching.Hybrid;
using TicketHub.BffKit;
using TicketHub.Contracts;
using TicketHub.DashboardBff.Clients;

namespace TicketHub.DashboardBff.Scoping;

/// <summary>Null EventIds means "everything" (Owner/Admin). Organizers always get an explicit list.</summary>
public sealed record DashboardScope(Guid UserId, string Role, IReadOnlyList<Guid>? EventIds)
{
    public bool Unrestricted => EventIds is null;

    public string CacheKey => Unrestricted ? "all" : $"org:{UserId:N}";

    public bool Allows(Guid eventId) => Unrestricted || EventIds!.Contains(eventId);
}

public sealed class ScopeResolver(HybridCache cache, AnalyticsApi analytics, EventsApi events)
{
    public async Task<DashboardScope> ResolveAsync(HttpContext ctx, CancellationToken ct)
    {
        var user = BffSession.ToUser(ctx.User);
        if (user.Role is Roles.Owner or Roles.Admin) return new DashboardScope(user.Id, user.Role, null);

        var ids = await cache.GetOrCreateAsync($"dash:scope:{user.Id:N}", async token =>
        {
            // Union of both sources: the read side knows historic events, the catalogue knows brand-new drafts.
            var results = await Task.WhenAll(
                Safe(() => analytics.EventIdsForOrganizerAsync(user.Id, token)),
                Safe(() => events.EventIdsForOrganizerAsync(user.Id, token)));
            return results.SelectMany(x => x).Distinct().ToArray();
        }, new HybridCacheEntryOptions { Expiration = TimeSpan.FromSeconds(60), LocalCacheExpiration = TimeSpan.FromSeconds(60) }, cancellationToken: ct);

        return new DashboardScope(user.Id, user.Role, ids);
    }

    private static async Task<IReadOnlyList<Guid>> Safe(Func<Task<IReadOnlyList<Guid>>> call)
    {
        try
        {
            return await call();
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }
}
