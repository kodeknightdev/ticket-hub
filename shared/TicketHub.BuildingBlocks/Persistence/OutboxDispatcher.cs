using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.Contracts;

namespace TicketHub.BuildingBlocks.Persistence;

/// <summary>
/// Polls the outbox table and publishes pending rows. Rows are claimed with a short lease
/// so several replicas can run the dispatcher without double-publishing in the common case.
/// </summary>
public sealed class OutboxDispatcher<TContext>(IServiceScopeFactory scopes, IEventPublisher publisher, ILogger<OutboxDispatcher<TContext>> logger)
    : BackgroundService where TContext : DbContext
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                published = await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Outbox dispatch failed");
            }

            if (published == 0)
                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var now = DateTimeOffset.UtcNow;

        var candidates = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null && (m.LockedUntil == null || m.LockedUntil < now) && m.Attempts < 20)
            .OrderBy(m => m.OccurredAt)
            .Take(50)
            .Select(m => m.Id)
            .ToListAsync(ct);

        var count = 0;
        foreach (var id in candidates)
        {
            var leaseUntil = DateTimeOffset.UtcNow.Add(Lease);
            var claimed = await db.Set<OutboxMessage>()
                .Where(m => m.Id == id && m.ProcessedAt == null && (m.LockedUntil == null || m.LockedUntil < now))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LockedUntil, leaseUntil), ct);
            if (claimed == 0) continue;

            var message = await db.Set<OutboxMessage>().AsNoTracking().FirstAsync(m => m.Id == id, ct);
            try
            {
                var envelope = JsonSerializer.Deserialize<EventEnvelope>(message.Payload, Json.Options)!;
                await publisher.PublishAsync(message.Topic, envelope, message.TraceParent, ct);
                await db.Set<OutboxMessage>().Where(m => m.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, DateTimeOffset.UtcNow).SetProperty(m => m.Attempts, m => m.Attempts + 1), ct);
                count++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Publishing outbox message {Id} failed", id);
                var error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                await db.Set<OutboxMessage>().Where(m => m.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.Attempts, m => m.Attempts + 1)
                        .SetProperty(m => m.LastError, error)
                        .SetProperty(m => m.LockedUntil, DateTimeOffset.UtcNow.AddSeconds(5)), ct);
            }
        }

        return count;
    }
}
