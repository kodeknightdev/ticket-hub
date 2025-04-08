using Microsoft.EntityFrameworkCore;
using TicketHub.Orders.Data;

namespace TicketHub.Orders.Ordering;

/// <summary>Backstop for orders whose hold-expired message never arrived.</summary>
public sealed class PendingOrderSweeper(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PendingOrderSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
                var cutoff = clock.GetUtcNow() - Grace;

                var stale = await db.Orders.Where(o => o.Status == OrderStatus.Pending && o.ExpiresAt < cutoff).Take(100).ToListAsync(stoppingToken);
                foreach (var order in stale) await orders.ExpireAsync(order, "hold-expired", stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Pending order sweep failed");
            }
        }
    }
}
