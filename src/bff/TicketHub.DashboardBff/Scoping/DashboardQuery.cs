using System.Text;

namespace TicketHub.DashboardBff.Scoping;

/// <summary>Normalised filter set. ToQuery always injects the scope so no downstream call can escape it.</summary>
public sealed record DashboardQuery(
    DateOnly? From, DateOnly? To, string? Granularity, string? City, string? Category, string? Venue, Guid? OrganizerId, IReadOnlyList<Guid> EventIds)
{
    public static DashboardQuery FromRequest(HttpRequest request)
    {
        var q = request.Query;
        static DateOnly? Date(string? v) => DateOnly.TryParse(v, out var d) ? d : null;
        var ids = (q["eventIds"].ToString() + "," + q["eventId"].ToString())
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).Distinct().ToList();
        return new DashboardQuery(
            Date(q["from"]), Date(q["to"]), Blank(q["granularity"]), Blank(q["city"]), Blank(q["category"]), Blank(q["venue"]),
            Guid.TryParse(q["organizerId"], out var org) ? org : null, ids);
    }

    public string ToQuery(DashboardScope scope, params (string Key, string? Value)[] extra)
    {
        var parts = new List<(string, string?)>
        {
            ("from", From?.ToString("yyyy-MM-dd")), ("to", To?.ToString("yyyy-MM-dd")), ("granularity", Granularity),
            ("city", City), ("category", Category), ("venue", Venue),
            ("organizerId", scope.Unrestricted ? OrganizerId?.ToString() : null)
        };

        var requested = EventIds.Count > 0 ? EventIds : null;
        IEnumerable<Guid>? effective = scope.Unrestricted ? requested : requested is null ? scope.EventIds : requested.Where(scope.Allows);
        if (effective is not null) parts.Add(("eventIds", string.Join(',', effective)));

        parts.AddRange(extra);
        var sb = new StringBuilder();
        foreach (var (key, value) in parts.Where(p => p.Item2 is not null))
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value!));
        }
        return sb.ToString();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
