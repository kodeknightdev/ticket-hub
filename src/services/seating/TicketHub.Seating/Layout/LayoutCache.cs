using System.Net;
using System.Text.Json;
using StackExchange.Redis;
using TicketHub.Contracts;

namespace TicketHub.Seating.Layout;

public interface ILayoutSource
{
    Task<EventLayout?> GetAsync(Guid eventId, CancellationToken ct = default);
}

public sealed class EventsLayoutClient(HttpClient http) : ILayoutSource
{
    public async Task<EventLayout?> GetAsync(Guid eventId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"/events/{eventId}/layout", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EventLayout>(Json.Options, ct);
    }
}

/// <summary>Cache-aside over the events service; layouts only change while an event is a draft.</summary>
public sealed class LayoutCache(IConnectionMultiplexer redis, ILayoutSource source)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public async Task<EventLayout?> GetAsync(Guid eventId, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = Key(eventId);
        var cached = await db.StringGetAsync(key);
        if (cached.HasValue) return JsonSerializer.Deserialize<EventLayout>(cached.ToString(), Json.Options);

        var layout = await source.GetAsync(eventId, ct);
        if (layout is not null)
            await db.StringSetAsync(key, JsonSerializer.Serialize(layout, Json.Options), Ttl);
        return layout;
    }

    public Task InvalidateAsync(Guid eventId) => redis.GetDatabase().KeyDeleteAsync(Key(eventId));

    private static string Key(Guid eventId) => $"seating:layout:{eventId}";
}
