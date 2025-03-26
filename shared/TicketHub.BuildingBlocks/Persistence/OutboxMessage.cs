namespace TicketHub.BuildingBlocks.Persistence;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public required string Topic { get; set; }
    public required string Key { get; set; }
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? TraceParent { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public required string Consumer { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
