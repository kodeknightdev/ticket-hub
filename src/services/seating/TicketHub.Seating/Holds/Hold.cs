using TicketHub.Contracts;

namespace TicketHub.Seating.Holds;

public sealed record Hold(
    Guid HoldId,
    Guid EventId,
    Guid BuyerId,
    string? BuyerEmail,
    IReadOnlyList<HeldSeat> Seats,
    DateTimeOffset HeldAt,
    DateTimeOffset ExpiresAt,
    Guid? ConfirmedOrderId = null);

public enum HoldOutcome
{
    Held,
    Conflict,
    Invalid,
    UnknownEvent
}

public sealed record HoldResult(HoldOutcome Outcome, Hold? Hold = null, IReadOnlyList<Guid>? Conflicts = null, string? Error = null);
