using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace TicketHub.TestSupport;

/// <summary>Keeps one in-memory SQLite connection open so every context in a test sees the same database.</summary>
public sealed class SqliteDb<TContext> : IDisposable where TContext : DbContext
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Func<DbContextOptions<TContext>, TContext> _factory;

    public SqliteDb(Func<DbContextOptions<TContext>, TContext> factory)
    {
        _factory = factory;
        _connection.Open();
        using var db = Create();
        db.Database.EnsureCreated();
    }

    public DbContextOptions<TContext> Options => new DbContextOptionsBuilder<TContext>().UseSqlite(_connection).Options;

    public SqliteConnection Connection => _connection;

    public TContext Create() => _factory(Options);

    public void Dispose() => _connection.Dispose();
}
