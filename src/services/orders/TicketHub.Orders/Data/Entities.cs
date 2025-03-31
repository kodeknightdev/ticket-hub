namespace TicketHub.Orders.Data;

public enum OrderStatus
{
    Pending,
    Paid,
    TicketsIssued,
    Expired,
    Failed,
    Refunded
}

public enum TicketStatus
{
    Valid,
    CheckedIn,
    Void
}

public sealed class Order
{
    public Guid Id { get; set; }
    public Guid BuyerId { get; set; }
    public string? BuyerEmail { get; set; }
    public Guid EventId { get; set; }
    public Guid HoldId { get; set; }
    public required string IdempotencyKey { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Fees { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "GBP";
    public string PaymentProvider { get; set; } = "mock";
    public string? PaymentRef { get; set; }
    public string? ClientSecret { get; set; }
    public string? StatusReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public byte[]? RowVersion { get; set; }

    public List<OrderLine> Lines { get; set; } = [];
    public List<Ticket> Tickets { get; set; } = [];
}

public sealed class OrderLine
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid SeatId { get; set; }
    public Guid TierId { get; set; }
    public Guid SectionId { get; set; }
    public required string Label { get; set; }
    public decimal Price { get; set; }
}

public sealed class Ticket
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid EventId { get; set; }
    public Guid BuyerId { get; set; }
    public Guid SeatId { get; set; }
    public required string Label { get; set; }
    public required string Code { get; set; }
    public TicketStatus Status { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? CheckedInAt { get; set; }
    public Guid? CheckedInBy { get; set; }
}

public sealed class PaymentWebhookEvent
{
    public required string Id { get; set; }
    public required string Provider { get; set; }
    public required string Type { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public enum SagaStatus
{
    Running,
    Completed,
    CompletedWithErrors
}

public enum SagaStepStage
{
    Pending,
    Refunded,
    TicketsVoided,
    Completed,
    Failed
}

public sealed class CancellationSaga
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public required string EventTitle { get; set; }
    public required string Reason { get; set; }
    public SagaStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<CancellationSagaStep> Steps { get; set; } = [];
}

public sealed class CancellationSagaStep
{
    public Guid Id { get; set; }
    public Guid SagaId { get; set; }
    public Guid OrderId { get; set; }
    public SagaStepStage Stage { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
