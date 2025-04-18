using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.Contracts;
using TicketHub.Orders.Data;
using TicketHub.Orders.Tickets;

namespace TicketHub.Orders.Ordering;

public enum CheckInOutcome
{
    Admitted,
    InvalidCode,
    WrongEvent,
    AlreadyCheckedIn,
    Voided
}

public sealed record CheckInResult(CheckInOutcome Outcome, TicketDto? Ticket = null, DateTimeOffset? PreviouslyAt = null);

public sealed class CheckInService(OrdersDbContext db, TicketSigner signer, TimeProvider clock)
{
    public async Task<CheckInResult> CheckInAsync(string code, Guid? expectedEventId, Guid staffId, CancellationToken ct = default)
    {
        var parsed = signer.Verify(code);
        if (parsed is null) return new CheckInResult(CheckInOutcome.InvalidCode);
        if (expectedEventId is { } eventId && eventId != parsed.EventId) return new CheckInResult(CheckInOutcome.WrongEvent);

        var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == parsed.TicketId, ct);
        if (ticket is null) return new CheckInResult(CheckInOutcome.InvalidCode);
        if (ticket.Status == TicketStatus.Void) return new CheckInResult(CheckInOutcome.Voided, ticket.ToDto());

        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Conditional update so two scanners at different doors can't both admit the same ticket.
        var updated = await db.Tickets
            .Where(t => t.Id == ticket.Id && t.Status == TicketStatus.Valid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TicketStatus.CheckedIn)
                .SetProperty(t => t.CheckedInAt, now)
                .SetProperty(t => t.CheckedInBy, staffId), ct);

        if (updated == 0)
        {
            await tx.RollbackAsync(ct);
            var current = await db.Tickets.AsNoTracking().FirstAsync(t => t.Id == ticket.Id, ct);
            return current.Status == TicketStatus.Void
                ? new CheckInResult(CheckInOutcome.Voided, current.ToDto())
                : new CheckInResult(CheckInOutcome.AlreadyCheckedIn, current.ToDto(), current.CheckedInAt);
        }

        db.Enqueue(Topics.TicketCheckedIn, ticket.EventId.ToString(), new TicketCheckedIn(ticket.Id, ticket.EventId, ticket.SeatId, staffId, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        ticket.Status = TicketStatus.CheckedIn;
        ticket.CheckedInAt = now;
        return new CheckInResult(CheckInOutcome.Admitted, ticket.ToDto());
    }
}
