using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;
using TicketHub.Orders.Data;
using TicketHub.Orders.Ordering;
using TicketHub.Orders.Payments;
using TicketHub.Orders.Tickets;
using TicketHub.TestSupport;

namespace TicketHub.Orders.Tests;

/// <summary>Runs against a real SQL Server so the unique indexes and conditional updates are what's under test.</summary>
public sealed class OrdersSqlServerTests : IAsyncLifetime
{
    private MsSqlContainer? _sql;
    private DbContextOptions<OrdersDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        if (new DockerFactAttribute().Skip is not null) return;
        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04").Build();
        await _sql.StartAsync();
        // same retry strategy as the service, so transactions are exercised the way production runs them
        _options = new DbContextOptionsBuilder<OrdersDbContext>().UseSqlServer(_sql.GetConnectionString(), s => s.EnableRetryOnFailure()).Options;
        await using var db = new OrdersDbContext(_options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_sql is not null) await _sql.DisposeAsync();
    }

    [DockerFact]
    public async Task Concurrent_creates_with_one_idempotency_key_produce_one_order()
    {
        var seating = new FakeSeating();
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var buyer = Guid.NewGuid();
        var hold = seating.AddHold(buyer, Guid.NewGuid(), 2, clock.GetUtcNow());
        var payments = new MockPaymentProvider(Options.Create(new MockPaymentOptions()), clock);
        var signer = new TicketSigner(Options.Create(new TicketOptions { SigningKey = "k-0123456789abcdef0123456789" }));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = new OrdersDbContext(_options);
            var service = new OrderService(db, seating, new FakePricing(), payments, signer, clock, NullLogger<OrderService>.Instance);
            return await service.CreateAsync(hold.HoldId, buyer, null, "double-click");
        }));

        results.Select(r => r.Order!.Id).Distinct().Count().ShouldBe(1);
        await using var check = new OrdersDbContext(_options);
        (await check.Orders.CountAsync()).ShouldBe(1);
    }

    [DockerFact]
    public async Task Two_scanners_racing_admit_a_ticket_once()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var signer = new TicketSigner(Options.Create(new TicketOptions { SigningKey = "k-0123456789abcdef0123456789" }));
        var ticketId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var code = signer.CreateCode(ticketId, eventId);

        await using (var db = new OrdersDbContext(_options))
        {
            var order = new Order
            {
                Id = Guid.NewGuid(), BuyerId = Guid.NewGuid(), EventId = eventId, HoldId = Guid.NewGuid(), IdempotencyKey = "seed",
                Status = OrderStatus.TicketsIssued, CreatedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow()
            };
            order.Tickets.Add(new Ticket { Id = ticketId, OrderId = order.Id, EventId = eventId, BuyerId = order.BuyerId, SeatId = Guid.NewGuid(), Label = "A1", Code = code, IssuedAt = clock.GetUtcNow() });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = new OrdersDbContext(_options);
            return (await new CheckInService(db, signer, clock).CheckInAsync(code, eventId, Guid.NewGuid())).Outcome;
        }));

        outcomes.Count(o => o == CheckInOutcome.Admitted).ShouldBe(1);
        outcomes.Count(o => o == CheckInOutcome.AlreadyCheckedIn).ShouldBe(5);
    }
}
