using TicketHub.Contracts;

namespace TicketHub.BuildingBlocks.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(string topic, EventEnvelope envelope, string? traceParent = null, CancellationToken ct = default);
}
