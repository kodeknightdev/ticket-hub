using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.Contracts;
using TicketHub.Seating.Data;
using TicketHub.Seating.Holds;
using TicketHub.Seating.Messaging;
using TicketHub.TestSupport;

namespace TicketHub.Seating.Tests;

public sealed class HoldTests(SeatingFixture fx) : IClassFixture<SeatingFixture>
{
    private readonly NullEventPublisher _publisher = new();

    private async Task<(Layout.EventLayout Layout, List<Guid> Seats)> NewEvent(int rows = 4, int seatsPerRow = 10)
    {
        var layout = SeatingFixture.Layout(Guid.NewGuid(), rows, seatsPerRow);
        await fx.RegisterEventAsync(layout, DateTimeOffset.UtcNow.AddDays(30));
        return (layout, layout.AllSeats().Select(s => s.Seat.Id).ToList());
    }

    [DockerFact]
    public async Task Held_seats_cannot_be_held_again()
    {
        var (layout, seats) = await NewEvent();
        await using var db = new SeatingDbContext(fx.SqlOptions);
        var service = fx.Service(db, layout, _publisher, TimeProvider.System);

        var first = await service.HoldAsync(layout.EventId, Guid.NewGuid(), null, seats[..3]);
        var second = await service.HoldAsync(layout.EventId, Guid.NewGuid(), null, seats[2..5]);

        first.Outcome.ShouldBe(HoldOutcome.Held);
        second.Outcome.ShouldBe(HoldOutcome.Conflict);
        second.Conflicts.ShouldBe([seats[2]]);
        _publisher.Published.Count(p => p.Topic == Topics.SeatHeld).ShouldBe(1);
    }

    [DockerFact]
    public async Task More_than_eight_seats_is_rejected()
    {
        var (layout, seats) = await NewEvent();
        await using var db = new SeatingDbContext(fx.SqlOptions);
        var result = await fx.Service(db, layout, _publisher, TimeProvider.System).HoldAsync(layout.EventId, Guid.NewGuid(), null, seats[..9]);
        result.Outcome.ShouldBe(HoldOutcome.Invalid);
    }

    [DockerFact]
    public async Task Release_frees_seats_and_only_touches_own_keys()
    {
        var (layout, seats) = await NewEvent();
        await using var db = new SeatingDbContext(fx.SqlOptions);
        var service = fx.Service(db, layout, _publisher, TimeProvider.System);
        var hold = (await service.HoldAsync(layout.EventId, Guid.NewGuid(), null, seats[..2])).Hold!;

        (await service.ReleaseAsync(hold, "test")).ShouldBeTrue();
        var again = await service.HoldAsync(layout.EventId, Guid.NewGuid(), null, seats[..2]);
        again.Outcome.ShouldBe(HoldOutcome.Held);

        // releasing the stale first hold must not free the new holder's seats
        (await service.ReleaseAsync(hold, "stale")).ShouldBeFalse();
        var snapshot = await service.AvailabilityAsync(layout.EventId);
        snapshot!.Held.ShouldBe(2);
    }

    [DockerFact]
    public async Task Expired_holds_release_seats_and_announce_once()
    {
        var (layout, seats) = await NewEvent();
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        await using var db = new SeatingDbContext(fx.SqlOptions);
        var service = fx.Service(db, layout, _publisher, clock, holdSeconds: 1);
        await service.HoldAsync(layout.EventId, Guid.NewGuid(), "buyer@tickethub.local", seats[..2]);

        await Task.Delay(1500);
        clock.Advance(TimeSpan.FromSeconds(2));
        var store = new HoldStore(fx.Redis);
        var worker = new HoldExpiryWorker(store, _publisher, clock, NullLogger<HoldExpiryWorker>.Instance);
        await Task.WhenAll(worker.SweepAsync(default), worker.SweepAsync(default));

        _publisher.Published.Count(p => p.Topic == Topics.HoldExpired && p.Envelope.Key == layout.EventId.ToString()).ShouldBe(1);
        (await service.AvailabilityAsync(layout.EventId))!.Available.ShouldBe(layout.Capacity);
    }

    [DockerFact]
    public async Task Confirmed_hold_survives_expiry_sweep()
    {
        var (layout, seats) = await NewEvent();
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        await using var db = new SeatingDbContext(fx.SqlOptions);
        var service = fx.Service(db, layout, _publisher, clock, holdSeconds: 1);
        var hold = (await service.HoldAsync(layout.EventId, Guid.NewGuid(), null, seats[..2])).Hold!;

        var store = new HoldStore(fx.Redis);
        (await store.ConfirmAsync(hold, Guid.NewGuid())).ShouldBeTrue();
        await Task.Delay(1500);
        clock.Advance(TimeSpan.FromSeconds(5));
        await new HoldExpiryWorker(store, _publisher, clock, NullLogger<HoldExpiryWorker>.Instance).SweepAsync(default);

        (await service.AvailabilityAsync(layout.EventId))!.Held.ShouldBe(2);
    }

    [DockerFact]
    public async Task Parallel_buyers_never_end_up_with_the_same_seat()
    {
        var (layout, seats) = await NewEvent(rows: 2, seatsPerRow: 10);
        var random = new Random(42);
        var attempts = Enumerable.Range(0, 60)
            .Select(_ => seats.OrderBy(_ => random.Next()).Take(random.Next(1, 5)).ToList())
            .ToList();

        var results = await Task.WhenAll(attempts.Select(async wanted =>
        {
            await using var db = new SeatingDbContext(fx.SqlOptions);
            return await fx.Service(db, layout, new NullEventPublisher(), TimeProvider.System).HoldAsync(layout.EventId, Guid.NewGuid(), null, wanted);
        }));

        var winners = results.Where(r => r.Outcome == HoldOutcome.Held).Select(r => r.Hold!).ToList();
        winners.ShouldNotBeEmpty();
        var heldSeats = winners.SelectMany(h => h.Seats.Select(s => s.SeatId)).ToList();
        heldSeats.Count.ShouldBe(heldSeats.Distinct().Count());

        // Pay for every winning hold, then replay one payment for a seat that's already sold.
        var publisher = new NullEventPublisher();
        foreach (var hold in winners)
        {
            await using var db = new SeatingDbContext(fx.SqlOptions);
            await new OrderPaidHandler(db, new HoldStore(fx.Redis), publisher, NullLogger<OrderPaidHandler>.Instance)
                .HandleAsync(Paid(hold, Guid.NewGuid()), default);
        }

        var victim = winners[0];
        await using (var db = new SeatingDbContext(fx.SqlOptions))
        {
            await Should.ThrowAsync<DbUpdateException>(() =>
                new OrderPaidHandler(db, new HoldStore(fx.Redis), publisher, NullLogger<OrderPaidHandler>.Instance)
                    .HandleAsync(Paid(victim, Guid.NewGuid()), default));
        }

        await using var check = new SeatingDbContext(fx.SqlOptions);
        var sold = await check.SoldSeats.Where(s => s.EventId == layout.EventId).ToListAsync();
        sold.Count.ShouldBe(heldSeats.Count);
        sold.Select(s => s.SeatId).Distinct().Count().ShouldBe(sold.Count);
        publisher.Published.Count(p => p.Topic == Topics.SeatsSold).ShouldBe(winners.Count);
    }

    [DockerFact]
    public async Task Sold_seats_show_as_sold_and_cannot_be_held()
    {
        var (layout, seats) = await NewEvent();
        await using var db = new SeatingDbContext(fx.SqlOptions);
        var service = fx.Service(db, layout, _publisher, TimeProvider.System);
        var hold = (await service.HoldAsync(layout.EventId, Guid.NewGuid(), null, seats[..2])).Hold!;

        await new OrderPaidHandler(db, new HoldStore(fx.Redis), _publisher, NullLogger<OrderPaidHandler>.Instance)
            .HandleAsync(Paid(hold, Guid.NewGuid()), default);

        (await service.AvailabilityAsync(layout.EventId))!.Sold.ShouldBe(2);
        (await service.HoldAsync(layout.EventId, Guid.NewGuid(), null, seats[..1])).Outcome.ShouldBe(HoldOutcome.Conflict);
    }

    private static EventEnvelope Paid(Hold hold, Guid orderId) => EventEnvelope.Create(Topics.OrderPaid, hold.EventId.ToString(), new OrderPaid(
        orderId, hold.HoldId, hold.EventId, hold.BuyerId,
        hold.Seats.Select(s => new OrderLineInfo(s.SeatId, s.TierId, s.SectionId, s.Label, 50m)).ToList(),
        50m * hold.Seats.Count, 0, 50m * hold.Seats.Count, "GBP", DateTimeOffset.UtcNow));
}
