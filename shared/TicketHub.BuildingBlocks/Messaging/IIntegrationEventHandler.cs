using TicketHub.Contracts;

namespace TicketHub.BuildingBlocks.Messaging;

public interface IIntegrationEventHandler
{
    Task HandleAsync(EventEnvelope envelope, CancellationToken ct);
}
