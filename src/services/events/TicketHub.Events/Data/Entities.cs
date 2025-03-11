namespace TicketHub.Events.Data;

public enum EventStatus
{
    Draft,
    Published,
    Cancelled
}

public sealed class Venue
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string City { get; set; }
}

public sealed class Event
{
    public Guid Id { get; set; }
    public Guid OrganizerId { get; set; }
    public required string Title { get; set; }
    public required string Artist { get; set; }
    public required string Category { get; set; }
    public required string City { get; set; }
    public Guid VenueId { get; set; }
    public required string VenueName { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Description { get; set; }
    public EventStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public byte[]? RowVersion { get; set; }

    public List<Section> Sections { get; set; } = [];
    public List<PriceTier> Tiers { get; set; } = [];

    public int Capacity => Sections.Sum(s => s.Seats.Count);
}

public sealed class Section
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public List<Seat> Seats { get; set; } = [];
}

public sealed class Seat
{
    public Guid Id { get; set; }
    public Guid SectionId { get; set; }
    public Guid EventId { get; set; }
    public required string Row { get; set; }
    public int Number { get; set; }
    public Guid TierId { get; set; }
}

public sealed class PriceTier
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public required string Name { get; set; }
    public decimal BasePrice { get; set; }
    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }
    public decimal CurrentPrice { get; set; }
}
