namespace TicketHub.Contracts;

public sealed record HeldSeat(Guid SeatId, Guid TierId, Guid SectionId, string Label);

public sealed record SeatHeld(
    Guid HoldId,
    Guid EventId,
    Guid BuyerId,
    string? BuyerEmail,
    IReadOnlyList<HeldSeat> Seats,
    DateTimeOffset HeldAt,
    DateTimeOffset ExpiresAt);

public sealed record SeatReleased(Guid HoldId, Guid EventId, IReadOnlyList<Guid> SeatIds, string Reason);

public sealed record HoldExpired(Guid HoldId, Guid EventId, Guid BuyerId, IReadOnlyList<Guid> SeatIds);

public sealed record HoldExpiring(Guid HoldId, Guid EventId, Guid BuyerId, string? BuyerEmail, DateTimeOffset ExpiresAt);

public sealed record SeatsSold(Guid EventId, Guid OrderId, Guid HoldId, IReadOnlyList<Guid> SeatIds, int SoldTotal);
