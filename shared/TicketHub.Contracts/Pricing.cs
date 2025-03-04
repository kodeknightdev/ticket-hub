namespace TicketHub.Contracts;

public sealed record PriceUpdated(
    Guid EventId,
    Guid TierId,
    decimal OldPrice,
    decimal NewPrice,
    double SellThrough,
    string Reason,
    DateTimeOffset ChangedAt);
