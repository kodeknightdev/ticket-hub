using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TicketHub.Contracts;
using TicketHub.Protos.Seating;

namespace TicketHub.DashboardBff.Clients;

/// <summary>Thin pass-through to the analytics service; responses stay as JSON so the BFF only reshapes what it must.</summary>
public sealed class AnalyticsApi(HttpClient http)
{
    public const string Name = "analytics";

    public async Task<JsonElement> GetAsync(string path, string query, CancellationToken ct)
    {
        var url = string.IsNullOrEmpty(query) ? path : $"{path}?{query}";
        return await http.GetFromJsonAsync<JsonElement>(url, Json.Options, ct);
    }

    public async Task<JsonElement?> GetOrNullAsync(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json.Options, ct);
    }

    public async Task<IReadOnlyList<Guid>> EventIdsForOrganizerAsync(Guid organizerId, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<Guid>>($"/analytics/events/ids?organizerId={organizerId}", Json.Options, ct) ?? [];
}

public sealed record EventSummaryDto(Guid Id, Guid OrganizerId, string Title, string Artist, string Category, string City, string VenueName,
    DateTimeOffset StartsAt, string Status, int Capacity);

public sealed class EventsApi(HttpClient http)
{
    public const string Name = "events";

    public async Task<IReadOnlyList<Guid>> EventIdsForOrganizerAsync(Guid organizerId, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<Guid>>($"/events/ids?organizerId={organizerId}", Json.Options, ct) ?? [];

    public async Task<JsonElement> StatsAsync(IReadOnlyCollection<Guid>? ids, CancellationToken ct) =>
        await http.GetFromJsonAsync<JsonElement>(ids is null ? "/events/stats" : $"/events/stats?ids={string.Join(',', ids)}", Json.Options, ct);

    public async Task<IReadOnlyList<EventSummaryDto>> SummariesAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var list = string.Join(',', ids.Distinct());
        if (list.Length == 0) return [];
        return await http.GetFromJsonAsync<List<EventSummaryDto>>($"/events/summaries?ids={list}", Json.Options, ct) ?? [];
    }
}

public sealed class PricingApi(HttpClient http)
{
    public const string Name = "pricing";

    public async Task<JsonElement> HistoryAsync(Guid eventId, CancellationToken ct) =>
        await http.GetFromJsonAsync<JsonElement>($"/prices/{eventId}/history", Json.Options, ct);
}

public sealed class OrdersApi(HttpClient http)
{
    public const string Name = "orders";

    public async Task<JsonElement> CancellationsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<JsonElement>("/cancellations", Json.Options, ct);
}

public interface ISeatingCounts
{
    Task<IReadOnlyDictionary<string, int>> SoldCountsAsync(IReadOnlyList<string> eventIds, CancellationToken ct);
}

public sealed class GrpcSeatingCounts(SeatingRpc.SeatingRpcClient client) : ISeatingCounts
{
    public async Task<IReadOnlyDictionary<string, int>> SoldCountsAsync(IReadOnlyList<string> eventIds, CancellationToken ct)
    {
        var request = new SoldCountsRequest();
        request.EventIds.AddRange(eventIds);
        var reply = await client.GetSoldCountsAsync(request, cancellationToken: ct);
        return reply.Counts.ToDictionary(c => c.EventId.ToLowerInvariant(), c => c.Sold);
    }
}
