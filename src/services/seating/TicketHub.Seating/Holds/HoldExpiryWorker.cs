using TicketHub.BuildingBlocks.Messaging;
using TicketHub.Contracts;

namespace TicketHub.Seating.Holds;

/// <summary>
/// Seat keys expire on their own in Redis; this worker only exists to emit the hold-expired
/// event. Replicas compete on ZREM, so each expiry is announced once.
/// </summary>
public sealed class HoldExpiryWorker(HoldStore store, IEventPublisher publisher, TimeProvider clock, ILogger<HoldExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Hold sweep failed");
            }
        }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        foreach (var holdId in await store.DueAsync(HoldStore.WarnIndex, now))
        {
            if (!await store.ClaimExpiredAsync(HoldStore.WarnIndex, holdId)) continue;
            var hold = await store.GetAsync(holdId);
            if (hold is null || hold.ConfirmedOrderId is not null) continue;
            await publisher.PublishAsync(Topics.HoldExpiring,
                EventEnvelope.Create(Topics.HoldExpiring, hold.EventId.ToString(),
                    new HoldExpiring(hold.HoldId, hold.EventId, hold.BuyerId, hold.BuyerEmail, hold.ExpiresAt)), ct: ct);
        }

        foreach (var holdId in await store.DueAsync(HoldStore.ExpiryIndex, now))
        {
            if (!await store.ClaimExpiredAsync(HoldStore.ExpiryIndex, holdId)) continue;
            var hold = await store.GetAsync(holdId);
            if (hold is null || hold.ConfirmedOrderId is not null) continue;

            await store.ReleaseAsync(hold);
            await publisher.PublishAsync(Topics.HoldExpired,
                EventEnvelope.Create(Topics.HoldExpired, hold.EventId.ToString(),
                    new HoldExpired(hold.HoldId, hold.EventId, hold.BuyerId, hold.Seats.Select(s => s.SeatId).ToList())), ct: ct);
            logger.LogInformation("Hold {HoldId} expired", holdId);
        }
    }
}
