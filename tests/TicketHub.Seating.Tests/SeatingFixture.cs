using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.Redis;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.Seating.Data;
using TicketHub.Seating.Holds;
using TicketHub.Seating.Layout;
using TicketHub.TestSupport;

namespace TicketHub.Seating.Tests;

public sealed class StaticLayout(EventLayout layout) : ILayoutSource
{
    public Task<EventLayout?> GetAsync(Guid eventId, CancellationToken ct = default) =>
        Task.FromResult<EventLayout?>(eventId == layout.EventId ? layout : null);
}

/// <summary>One Redis and one SQL Server container shared by the class; each test uses fresh ids.</summary>
public sealed class SeatingFixture : IAsyncLifetime
{
    private RedisContainer? _redis;
    private MsSqlContainer? _sql;

    public IConnectionMultiplexer Redis { get; private set; } = null!;
    public DbContextOptions<SeatingDbContext> SqlOptions { get; private set; } = null!;
    public bool Ready { get; private set; }

    public async Task InitializeAsync()
    {
        if (new DockerFactAttribute().Skip is not null) return;
        _redis = new RedisBuilder().WithImage("redis:7.4.2-alpine").Build();
        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04").Build();
        await Task.WhenAll(_redis.StartAsync(), _sql.StartAsync());

        Redis = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        SqlOptions = new DbContextOptionsBuilder<SeatingDbContext>().UseSqlServer(_sql.GetConnectionString()).Options;
        await using var db = new SeatingDbContext(SqlOptions);
        await db.Database.MigrateAsync();
        Ready = true;
    }

    public async Task DisposeAsync()
    {
        Redis?.Dispose();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_sql is not null) await _sql.DisposeAsync();
    }

    public static EventLayout Layout(Guid eventId, int rows = 4, int seatsPerRow = 10)
    {
        var tier = Guid.NewGuid();
        var sections = new[] { "Floor" }.Select((name, i) => new LayoutSection(Guid.NewGuid(), name, i,
            Enumerable.Range(0, rows).SelectMany(r => Enumerable.Range(1, seatsPerRow)
                .Select(n => new LayoutSeat(Guid.NewGuid(), ((char)('A' + r)).ToString(), n, tier))).ToList())).ToList();
        return new EventLayout(eventId, rows * seatsPerRow, sections, [new LayoutTier(tier, "Standard", 50, 40, 90, 50)]);
    }

    public async Task<Guid> RegisterEventAsync(EventLayout layout, DateTimeOffset startsAt)
    {
        await using var db = new SeatingDbContext(SqlOptions);
        db.Events.Add(new SeatingEvent { EventId = layout.EventId, Title = "Test", Capacity = layout.Capacity, StartsAt = startsAt });
        await db.SaveChangesAsync();
        return layout.EventId;
    }

    public HoldService Service(SeatingDbContext db, EventLayout layout, IEventPublisher publisher, TimeProvider clock, int holdSeconds = 600) =>
        new(new HoldStore(Redis), new LayoutCache(Redis, new StaticLayout(layout)), db, publisher,
            Options.Create(new SeatingOptions { HoldSeconds = holdSeconds, MaxSeatsPerHold = 8, WarnSecondsBeforeExpiry = 0 }),
            clock, NullLogger<HoldService>.Instance);
}
