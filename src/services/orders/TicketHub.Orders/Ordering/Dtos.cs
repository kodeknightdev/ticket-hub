using TicketHub.Orders.Data;

namespace TicketHub.Orders.Ordering;

public sealed record OrderLineDto(Guid SeatId, Guid TierId, string Label, decimal Price);
public sealed record PaymentDto(string Provider, string? PaymentRef, string? ClientSecret, string? PublishableKey);
public sealed record OrderDto(
    Guid Id, Guid EventId, Guid HoldId, string Status, string? StatusReason, decimal Subtotal, decimal Fees, decimal Total,
    string Currency, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? PaidAt,
    IReadOnlyList<OrderLineDto> Lines, PaymentDto? Payment);

public sealed record TicketDto(Guid Id, Guid OrderId, Guid EventId, Guid SeatId, string Label, string Code, string Status,
    DateTimeOffset IssuedAt, DateTimeOffset? CheckedInAt, string? QrPngBase64);

public sealed record QuoteLine(Guid SeatId, Guid TierId, string Label, decimal Price);
public sealed record QuoteDto(Guid HoldId, Guid EventId, IReadOnlyList<QuoteLine> Lines, decimal Subtotal, decimal Fees, decimal Total, string Currency, OrderDto? ExistingOrder);

public static class OrderMapping
{
    public static OrderDto ToDto(this Order o, string? publishableKey = null, bool includeSecret = false) => new(
        o.Id, o.EventId, o.HoldId, o.Status.ToString(), o.StatusReason, o.Subtotal, o.Fees, o.Total, o.Currency,
        o.CreatedAt, o.ExpiresAt, o.PaidAt,
        o.Lines.Select(l => new OrderLineDto(l.SeatId, l.TierId, l.Label, l.Price)).ToList(),
        new PaymentDto(o.PaymentProvider, o.PaymentRef, includeSecret && o.Status == OrderStatus.Pending ? o.ClientSecret : null, publishableKey));

    public static TicketDto ToDto(this Ticket t, bool withQr = false) => new(
        t.Id, t.OrderId, t.EventId, t.SeatId, t.Label, t.Code, t.Status.ToString(), t.IssuedAt, t.CheckedInAt,
        withQr && t.Status != TicketStatus.Void ? Tickets.TicketSigner.RenderQrPngBase64(t.Code) : null);
}

public static class Fees
{
    public const decimal PerTicket = 1.50m;
    public const decimal Percent = 0.05m;

    public static decimal For(decimal subtotal, int tickets) =>
        Math.Round(subtotal * Percent + PerTicket * tickets, 2, MidpointRounding.AwayFromZero);
}
