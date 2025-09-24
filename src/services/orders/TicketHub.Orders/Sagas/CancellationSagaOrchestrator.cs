using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.Contracts;
using TicketHub.Orders.Data;
using TicketHub.Orders.Payments;

namespace TicketHub.Orders.Sagas;

/// <summary>
/// Orchestrates refunds when an event is cancelled. Each step's stage is persisted after every
/// action, so a crash mid-way resumes from the last completed stage instead of starting over.
/// </summary>
public sealed class CancellationSagaOrchestrator(OrdersDbContext db, IPaymentProvider payments, TimeProvider clock, ILogger<CancellationSagaOrchestrator> logger)
{
    public const int MaxAttempts = 5;

    public async Task<CancellationSaga> StartAsync(EventCancelled cancelled, CancellationToken ct = default)
    {
        var existing = await db.CancellationSagas.FirstOrDefaultAsync(s => s.EventId == cancelled.EventId, ct);
        if (existing is not null) return existing;

        var now = clock.GetUtcNow();
        var saga = new CancellationSaga
        {
            Id = Guid.NewGuid(),
            EventId = cancelled.EventId,
            EventTitle = cancelled.Title,
            Reason = cancelled.Reason,
            Status = SagaStatus.Running,
            StartedAt = now
        };

        var paidOrders = await db.Orders
            .Where(o => o.EventId == cancelled.EventId && (o.Status == OrderStatus.Paid || o.Status == OrderStatus.TicketsIssued))
            .Select(o => o.Id)
            .ToListAsync(ct);
        saga.Steps = paidOrders.Select(id => new CancellationSagaStep { Id = Guid.NewGuid(), SagaId = saga.Id, OrderId = id, Stage = SagaStepStage.Pending, UpdatedAt = now }).ToList();

        var pending = await db.Orders.Where(o => o.EventId == cancelled.EventId && o.Status == OrderStatus.Pending).ToListAsync(ct);
        foreach (var order in pending)
        {
            order.Status = OrderStatus.Expired;
            order.StatusReason = "event-cancelled";
            db.Enqueue(Topics.OrderExpired, order.EventId.ToString(), new OrderExpired(order.Id, order.HoldId, order.EventId, "event-cancelled"));
        }

        if (saga.Steps.Count == 0)
        {
            saga.Status = SagaStatus.Completed;
            saga.CompletedAt = now;
        }

        db.CancellationSagas.Add(saga);
        logger.LogInformation("Cancellation saga {SagaId} started for event {EventId} with {Count} orders", saga.Id, saga.EventId, saga.Steps.Count);
        return saga;
    }

    public async Task<int> RunPendingAsync(CancellationToken ct = default)
    {
        var sagaIds = await db.CancellationSagas.Where(s => s.Status == SagaStatus.Running).Select(s => s.Id).ToListAsync(ct);
        var processed = 0;
        foreach (var sagaId in sagaIds)
            processed += await RunAsync(sagaId, ct);
        return processed;
    }

    public async Task<int> RunAsync(Guid sagaId, CancellationToken ct = default)
    {
        var saga = await db.CancellationSagas.Include(s => s.Steps).FirstAsync(s => s.Id == sagaId, ct);
        var processed = 0;

        foreach (var step in saga.Steps.Where(s => s.Stage is not (SagaStepStage.Completed or SagaStepStage.Failed)))
        {
            try
            {
                await AdvanceAsync(step, ct);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                step.Attempts++;
                step.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                step.UpdatedAt = clock.GetUtcNow();
                if (step.Attempts >= MaxAttempts) step.Stage = SagaStepStage.Failed;
                await db.SaveChangesAsync(ct);
                logger.LogWarning(ex, "Saga {SagaId} step for order {OrderId} failed (attempt {Attempt})", saga.Id, step.OrderId, step.Attempts);
            }
        }

        if (saga.Steps.All(s => s.Stage is SagaStepStage.Completed or SagaStepStage.Failed))
        {
            saga.Status = saga.Steps.Any(s => s.Stage == SagaStepStage.Failed) ? SagaStatus.CompletedWithErrors : SagaStatus.Completed;
            saga.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return processed;
    }

    private async Task AdvanceAsync(CancellationSagaStep step, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Tickets).FirstAsync(o => o.Id == step.OrderId, ct);

        if (step.Stage == SagaStepStage.Pending)
        {
            if (order.Status != OrderStatus.Refunded)
            {
                var refund = await payments.RefundAsync(order.PaymentRef!, order.Total, $"cancel-{order.Id:N}", ct);
                if (!refund.Succeeded) throw new InvalidOperationException(refund.Error ?? "Refund failed");
            }
            step.Stage = SagaStepStage.Refunded;
            step.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        if (step.Stage == SagaStepStage.Refunded)
        {
            foreach (var ticket in order.Tickets) ticket.Status = TicketStatus.Void;
            step.Stage = SagaStepStage.TicketsVoided;
            step.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        if (step.Stage == SagaStepStage.TicketsVoided)
        {
            var now = clock.GetUtcNow();
            var alreadyRefunded = order.Status == OrderStatus.Refunded;
            order.Status = OrderStatus.Refunded;
            order.StatusReason = "event-cancelled";
            order.RefundedAt ??= now;

            // The refund event doubles as the buyer notification; notifier keys the email off the reason.
            if (!alreadyRefunded)
                db.Enqueue(Topics.OrderRefunded, order.EventId.ToString(), new OrderRefunded(
                    order.Id, order.EventId, order.BuyerId, order.BuyerEmail, order.Total, order.Tickets.Count, "event-cancelled", now));

            step.Stage = SagaStepStage.Completed;
            step.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
    }
}

public sealed class CancellationSagaWorker(IServiceScopeFactory scopes, ILogger<CancellationSagaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CancellationSagaOrchestrator>().RunPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Cancellation saga sweep failed");
            }
        }
    }
}
