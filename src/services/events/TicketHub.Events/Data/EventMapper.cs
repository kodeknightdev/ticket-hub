using TicketHub.Contracts;

namespace TicketHub.Events.Data;

public sealed record TierDto(Guid Id, string Name, decimal BasePrice, decimal MinPrice, decimal MaxPrice, decimal CurrentPrice);
public sealed record SeatDto(Guid Id, string Row, int Number, Guid TierId);
public sealed record SectionDto(Guid Id, string Name, int SortOrder, IReadOnlyList<SeatDto> Seats);

public sealed record EventSummaryDto(
    Guid Id, Guid OrganizerId, string Title, string Artist, string Category, string City, Guid VenueId, string VenueName,
    DateTimeOffset StartsAt, string? CoverImageUrl, string Status, int Capacity, decimal FromPrice);

public sealed record EventDetailDto(
    Guid Id, Guid OrganizerId, string Title, string Artist, string Category, string City, Guid VenueId, string VenueName,
    DateTimeOffset StartsAt, string? CoverImageUrl, string? Description, string Status, int Capacity,
    DateTimeOffset? PublishedAt, IReadOnlyList<TierDto> Tiers);

public sealed record LayoutDto(Guid EventId, int Capacity, IReadOnlyList<SectionDto> Sections, IReadOnlyList<TierDto> Tiers);

public static class EventMapper
{
    public static TierDto ToDto(this PriceTier t) => new(t.Id, t.Name, t.BasePrice, t.MinPrice, t.MaxPrice, t.CurrentPrice);

    public static EventSummaryDto ToSummary(this Event e, int capacity) => new(
        e.Id, e.OrganizerId, e.Title, e.Artist, e.Category, e.City, e.VenueId, e.VenueName, e.StartsAt, e.CoverImageUrl,
        e.Status.ToString(), capacity, e.Tiers.Count == 0 ? 0 : e.Tiers.Min(t => t.CurrentPrice));

    public static EventDetailDto ToDetail(this Event e) => new(
        e.Id, e.OrganizerId, e.Title, e.Artist, e.Category, e.City, e.VenueId, e.VenueName, e.StartsAt, e.CoverImageUrl,
        e.Description, e.Status.ToString(), e.Capacity, e.PublishedAt, e.Tiers.Select(ToDto).ToList());

    public static LayoutDto ToLayout(this Event e) => new(
        e.Id,
        e.Capacity,
        e.Sections.OrderBy(s => s.SortOrder).Select(s => new SectionDto(s.Id, s.Name, s.SortOrder,
            s.Seats.OrderBy(x => x.Row).ThenBy(x => x.Number).Select(x => new SeatDto(x.Id, x.Row, x.Number, x.TierId)).ToList())).ToList(),
        e.Tiers.Select(ToDto).ToList());

    public static EventPublished ToPublished(this Event e) => new(
        e.Id, e.OrganizerId, e.Title, e.Artist, e.Category, e.City, e.VenueId, e.VenueName, e.StartsAt,
        e.PublishedAt ?? DateTimeOffset.UtcNow, e.Capacity,
        e.Sections.Select(s => new SectionInfo(s.Id, s.Name, s.Seats.Count)).ToList(),
        e.Tiers.Select(t => new TierInfo(t.Id, t.Name, t.BasePrice, t.MinPrice, t.MaxPrice,
            e.Sections.SelectMany(s => s.Seats).Count(x => x.TierId == t.Id))).ToList());
}
