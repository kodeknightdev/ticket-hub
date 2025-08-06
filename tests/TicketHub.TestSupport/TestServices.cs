using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TicketHub.TestSupport;

public static class TestServices
{
    /// <summary>Swaps a SQL Server DbContext registration for a shared in-memory SQLite connection.</summary>
    public static IServiceCollection UseSqliteFor<TContext>(this IServiceCollection services, SqliteConnection connection) where TContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<TContext>>();
        services.AddDbContext<TContext>(o => o.UseSqlite(connection));
        return services;
    }
}
