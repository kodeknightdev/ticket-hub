namespace TicketHub.Contracts;

public sealed record OrderLineInfo(Guid SeatId, Guid TierId, Guid SectionId, string Label, decimal Price);

public sealed record OrderCreated(Guid OrderId, Guid HoldId, Guid EventId, Guid BuyerId, decimal Total, string Currency, DateTimeOffset CreatedAt);

public sealed record OrderPaid(
    Guid OrderId,
    Guid HoldId,
    Guid EventId,
    Guid BuyerId,
    IReadOnlyList<OrderLineInfo> Lines,
    decimal Subtotal,
    decimal Fees,
    decimal Total,
    string Currency,
    DateTimeOffset PaidAt);

public sealed record OrderExpired(Guid OrderId, Guid HoldId, Guid EventId, string Reason);

public sealed record OrderRefunded(
    Guid OrderId,
    Guid EventId,
    Guid BuyerId,
    string? BuyerEmail,
    decimal Amount,
    int TicketCount,
    string Reason,
    DateTimeOffset RefundedAt);

public sealed record IssuedTicket(Guid TicketId, Guid SeatId, string Label, string Code);

public sealed record TicketsIssued(
    Guid OrderId,
    Guid EventId,
    Guid BuyerId,
    string? BuyerEmail,
    IReadOnlyList<IssuedTicket> Tickets,
    DateTimeOffset IssuedAt);

public sealed record TicketCheckedIn(Guid TicketId, Guid EventId, Guid SeatId, Guid StaffId, DateTimeOffset CheckedInAt);
