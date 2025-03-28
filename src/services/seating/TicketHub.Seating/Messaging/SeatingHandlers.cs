using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.Contracts;
using TicketHub.Seating.Data;
using TicketHub.Seating.Holds;
using TicketHub.Seating.Layout;

namespace TicketHub.Seating.Messaging;

public sealed class OrderPaidHandler(SeatingDbContext db, HoldStore store, IEventPublisher publisher, ILogger<OrderPaidHandler> logger) : IIntegrationEventHandler
{
    private const string Consumer = "seating.order-paid";

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (await db.AlreadyProcessedAsync(envelope.Id, Consumer, ct)) return;

        var paid = envelope.As<OrderPaid>();
        var seatIds = paid.Lines.Select(l => l.SeatId).ToList();
        var now = DateTimeOffset.UtcNow;

        foreach (var seatId in seatIds)
            db.SoldSeats.Add(new SoldSeat { EventId = paid.EventId, SeatId = seatId, OrderId = paid.OrderId, SoldAt = now });
        db.MarkProcessed(envelope.Id, Consumer);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsDuplicateKey())
        {
            db.ChangeTracker.Clear();
            var already = await db.SoldSeats.Where(s => s.EventId == paid.EventId && seatIds.Contains(s.SeatId)).ToListAsync(ct);
            if (already.All(s => s.OrderId == paid.OrderId)) return;

            // Should be impossible while holds work; surfaces loudly if it ever happens.
            logger.LogCritical("Seats for order {OrderId} were already sold to another order", paid.OrderId);
            throw;
        }

        await store.MarkSoldAsync(paid.EventId, paid.HoldId, seatIds);
        var soldTotal = await db.SoldSeats.CountAsync(s => s.EventId == paid.EventId, ct);

        await publisher.PublishAsync(Topics.SeatsSold,
            EventEnvelope.Create(Topics.SeatsSold, paid.EventId.ToString(), new SeatsSold(paid.EventId, paid.OrderId, paid.HoldId, seatIds, soldTotal)), ct: ct);
    }
}

public sealed class EventLifecycleHandler(SeatingDbContext db, LayoutCache layouts, HoldStore store, HoldService holds) : IIntegrationEventHandler
{
    private const string Consumer = "seating.event-lifecycle";

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (await db.AlreadyProcessedAsync(envelope.Id, Consumer, ct)) return;

        switch (envelope.Type)
        {
            case Topics.EventPublished:
            {
                var published = envelope.As<EventPublished>();
                var existing = await db.Events.FirstOrDefaultAsync(e => e.EventId == published.EventId, ct);
                if (existing is null)
                    db.Events.Add(new SeatingEvent { EventId = published.EventId, Title = published.Title, Capacity = published.Capacity, StartsAt = published.StartsAt });
                await layouts.InvalidateAsync(published.EventId);
                break;
            }
            case Topics.EventUpdated:
            {
                var updated = envelope.As<EventUpdated>();
                var existing = await db.Events.FirstOrDefaultAsync(e => e.EventId == updated.EventId, ct);
                if (existing is not null)
                {
                    existing.Title = updated.Title;
                    existing.StartsAt = updated.StartsAt;
                }
                break;
            }
            case Topics.EventCancelled:
            {
                var cancelled = envelope.As<EventCancelled>();
                var existing = await db.Events.FirstOrDefaultAsync(e => e.EventId == cancelled.EventId, ct);
                if (existing is not null) existing.Cancelled = true;

                foreach (var holdId in await store.HoldsForEventAsync(cancelled.EventId))
                {
                    var hold = await store.GetAsync(holdId);
                    if (hold is not null && hold.ConfirmedOrderId is null) await holds.ReleaseAsync(hold, "event-cancelled", ct);
                }
                break;
            }
        }

        db.MarkProcessed(envelope.Id, Consumer);
        await db.SaveChangesAsync(ct);
    }
}
