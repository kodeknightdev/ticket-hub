namespace TicketHub.Contracts;

public sealed record SectionInfo(Guid SectionId, string Name, int Capacity);

public sealed record TierInfo(Guid TierId, string Name, decimal BasePrice, decimal MinPrice, decimal MaxPrice, int Capacity);

public sealed record EventPublished(
    Guid EventId,
    Guid OrganizerId,
    string Title,
    string Artist,
    string Category,
    string City,
    Guid VenueId,
    string VenueName,
    DateTimeOffset StartsAt,
    DateTimeOffset PublishedAt,
    int Capacity,
    IReadOnlyList<SectionInfo> Sections,
    IReadOnlyList<TierInfo> Tiers);

public sealed record EventUpdated(
    Guid EventId,
    string Title,
    string Artist,
    string Category,
    string City,
    DateTimeOffset StartsAt,
    string? CoverImageUrl);

public sealed record EventCancelled(Guid EventId, Guid OrganizerId, string Title, string Reason, DateTimeOffset CancelledAt);
