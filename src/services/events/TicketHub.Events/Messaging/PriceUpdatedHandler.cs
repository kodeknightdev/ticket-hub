using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.Contracts;
using TicketHub.Events.Data;

namespace TicketHub.Events.Messaging;

public sealed class PriceUpdatedHandler(EventsDbContext db, ILogger<PriceUpdatedHandler> logger) : IIntegrationEventHandler
{
    private const string Consumer = "events.price-updated";

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (await db.AlreadyProcessedAsync(envelope.Id, Consumer, ct)) return;

        var change = envelope.As<PriceUpdated>();
        var tier = await db.Tiers.FirstOrDefaultAsync(t => t.Id == change.TierId && t.EventId == change.EventId, ct);
        if (tier is null)
        {
            logger.LogWarning("Price update for unknown tier {TierId}", change.TierId);
        }
        else
        {
            tier.CurrentPrice = Math.Clamp(change.NewPrice, tier.MinPrice, tier.MaxPrice);
        }

        db.MarkProcessed(envelope.Id, Consumer);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsDuplicateKey())
        {
            // another replica got there first
        }
    }
}
