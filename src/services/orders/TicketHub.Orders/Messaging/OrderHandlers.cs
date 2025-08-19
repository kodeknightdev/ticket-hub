using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.Contracts;
using TicketHub.Orders.Data;
using TicketHub.Orders.Ordering;
using TicketHub.Orders.Sagas;

namespace TicketHub.Orders.Messaging;

public sealed class SeatsSoldHandler(OrdersDbContext db, OrderService orders) : IIntegrationEventHandler
{
    private const string Consumer = "orders.seats-sold";

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (await db.AlreadyProcessedAsync(envelope.Id, Consumer, ct)) return;

        var sold = envelope.As<SeatsSold>();
        var order = await db.Orders.Include(o => o.Lines).Include(o => o.Tickets).FirstOrDefaultAsync(o => o.Id == sold.OrderId, ct);
        if (order is not null) orders.IssueTickets(order);

        db.MarkProcessed(envelope.Id, Consumer);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class HoldExpiredHandler(OrdersDbContext db) : IIntegrationEventHandler
{
    private const string Consumer = "orders.hold-expired";

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (await db.AlreadyProcessedAsync(envelope.Id, Consumer, ct)) return;

        var expired = envelope.As<HoldExpired>();
        var pending = await db.Orders.Where(o => o.HoldId == expired.HoldId && o.Status == OrderStatus.Pending).ToListAsync(ct);
        foreach (var order in pending)
        {
            order.Status = OrderStatus.Expired;
            order.StatusReason = "hold-expired";
            db.Enqueue(Topics.OrderExpired, order.EventId.ToString(), new OrderExpired(order.Id, order.HoldId, order.EventId, "hold-expired"));
        }

        db.MarkProcessed(envelope.Id, Consumer);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class EventCancelledHandler(OrdersDbContext db, CancellationSagaOrchestrator orchestrator) : IIntegrationEventHandler
{
    private const string Consumer = "orders.event-cancelled";

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (await db.AlreadyProcessedAsync(envelope.Id, Consumer, ct)) return;
        await orchestrator.StartAsync(envelope.As<EventCancelled>(), ct);
        db.MarkProcessed(envelope.Id, Consumer);
        await db.SaveChangesAsync(ct);
    }
}
