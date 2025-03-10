using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TicketHub.BuildingBlocks.Hosting;

public static class DatabaseStartup
{
    /// <summary>
    /// Applies migrations and runs the seeder in Development, or when Database:AutoMigrate is true.
    /// </summary>
    public static async Task MigrateAndSeedAsync<TContext>(this WebApplication app, Func<TContext, IServiceProvider, Task>? seed = null)
        where TContext : DbContext
    {
        var enabled = app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:AutoMigrate");
        if (!enabled) return;

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");

        // SQL Server in compose can take a while after the healthcheck flips green
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (db.Database.IsRelational() && db.Database.GetMigrations().Any())
                    await db.Database.MigrateAsync();
                else
                    await db.Database.EnsureCreatedAsync();
                break;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "Database not ready (attempt {Attempt}), retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        if (seed is not null) await seed(db, scope.ServiceProvider);
    }
}
