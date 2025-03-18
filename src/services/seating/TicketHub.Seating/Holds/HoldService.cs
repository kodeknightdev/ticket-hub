using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.Contracts;
using TicketHub.Seating.Data;
using TicketHub.Seating.Layout;

namespace TicketHub.Seating.Holds;

public sealed record SeatAvailability(Guid SeatId, string State);

public sealed record AvailabilitySnapshot(Guid EventId, int Capacity, int Available, int Held, int Sold, IReadOnlyList<SeatAvailability> Seats);

public sealed class HoldService(
    HoldStore store,
    LayoutCache layouts,
    SeatingDbContext db,
    IEventPublisher publisher,
    IOptions<SeatingOptions> options,
    TimeProvider clock,
    ILogger<HoldService> logger)
{
    private readonly SeatingOptions _options = options.Value;

    public async Task<HoldResult> HoldAsync(Guid eventId, Guid buyerId, string? buyerEmail, IReadOnlyList<Guid> seatIds, CancellationToken ct = default)
    {
        var distinct = seatIds.Distinct().ToList();
        if (distinct.Count == 0 || distinct.Count > _options.MaxSeatsPerHold)
            return new HoldResult(HoldOutcome.Invalid, Error: $"Select between 1 and {_options.MaxSeatsPerHold} seats");

        var evt = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId, ct);
        if (evt is null || evt.Cancelled) return new HoldResult(HoldOutcome.UnknownEvent, Error: "Event is not on sale");
        if (evt.StartsAt <= clock.GetUtcNow()) return new HoldResult(HoldOutcome.Invalid, Error: "Event has already started");

        var layout = await layouts.GetAsync(eventId, ct);
        if (layout is null) return new HoldResult(HoldOutcome.UnknownEvent, Error: "Seat map not found");

        var seatLookup = layout.AllSeats().ToDictionary(x => x.Seat.Id);
        var unknown = distinct.Where(id => !seatLookup.ContainsKey(id)).ToList();
        if (unknown.Count > 0) return new HoldResult(HoldOutcome.Invalid, Conflicts: unknown, Error: "Unknown seats");

        await EnsureSoldLoadedAsync(eventId, ct);

        var now = clock.GetUtcNow();
        var ttl = TimeSpan.FromSeconds(_options.HoldSeconds);
        var hold = new Hold(
            Guid.NewGuid(), eventId, buyerId, buyerEmail,
            distinct.Select(id =>
            {
                var (section, seat) = seatLookup[id];
                return new HeldSeat(id, seat.TierId, section.Id, $"{section.Name} {seat.Row}{seat.Number}");
            }).ToList(),
            now, now.Add(ttl));

        var conflicts = await store.TryHoldAsync(hold, ttl, TimeSpan.FromSeconds(_options.WarnSecondsBeforeExpiry));
        if (conflicts.Count > 0) return new HoldResult(HoldOutcome.Conflict, Conflicts: conflicts, Error: "Some seats were just taken");

        await publisher.PublishAsync(Topics.SeatHeld,
            EventEnvelope.Create(Topics.SeatHeld, eventId.ToString(),
                new SeatHeld(hold.HoldId, eventId, buyerId, buyerEmail, hold.Seats, hold.HeldAt, hold.ExpiresAt)), ct: ct);

        logger.LogInformation("Hold {HoldId} placed on {Count} seats for event {EventId}", hold.HoldId, hold.Seats.Count, eventId);
        return new HoldResult(HoldOutcome.Held, hold);
    }

    public async Task<bool> ReleaseAsync(Hold hold, string reason, CancellationToken ct = default)
    {
        var released = await store.ReleaseAsync(hold);
        if (released == 0) return false;

        await publisher.PublishAsync(Topics.SeatReleased,
            EventEnvelope.Create(Topics.SeatReleased, hold.EventId.ToString(),
                new SeatReleased(hold.HoldId, hold.EventId, hold.Seats.Select(s => s.SeatId).ToList(), reason)), ct: ct);
        return true;
    }

    public async Task<AvailabilitySnapshot?> AvailabilityAsync(Guid eventId, CancellationToken ct = default)
    {
        var layout = await layouts.GetAsync(eventId, ct);
        if (layout is null) return null;

        await EnsureSoldLoadedAsync(eventId, ct);
        var seatIds = layout.AllSeats().Select(x => x.Seat.Id).ToList();
        var states = await store.SeatStatesAsync(eventId, seatIds);

        return new AvailabilitySnapshot(
            eventId,
            seatIds.Count,
            states.Values.Count(s => s == SeatStates.Available),
            states.Values.Count(s => s == SeatStates.Held),
            states.Values.Count(s => s == SeatStates.Sold),
            states.Select(kv => new SeatAvailability(kv.Key, kv.Value)).ToList());
    }

    public async Task EnsureSoldLoadedAsync(Guid eventId, CancellationToken ct = default)
    {
        if (await store.IsSoldSetLoadedAsync(eventId)) return;
        var sold = await db.SoldSeats.AsNoTracking().Where(s => s.EventId == eventId).Select(s => s.SeatId).ToListAsync(ct);
        await store.LoadSoldSetAsync(eventId, sold);
    }
}
