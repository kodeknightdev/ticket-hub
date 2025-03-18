using TicketHub.BuildingBlocks.Security;
using TicketHub.Contracts;
using TicketHub.Seating.Holds;

namespace TicketHub.Seating.Endpoints;

public sealed record HoldRequest(Guid EventId, IReadOnlyList<Guid> SeatIds);

public sealed record HoldResponse(Guid HoldId, Guid EventId, IReadOnlyList<HeldSeat> Seats, DateTimeOffset HeldAt, DateTimeOffset ExpiresAt, int SecondsLeft);

public static class SeatingEndpoints
{
    public static IEndpointRouteBuilder MapSeatingEndpoints(this IEndpointRouteBuilder app)
    {
        var seating = app.MapGroup("/seating").WithTags("Seating");

        seating.MapGet("/events/{eventId:guid}/availability", async (Guid eventId, HoldService holds, CancellationToken ct) =>
        {
            var snapshot = await holds.AvailabilityAsync(eventId, ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        });

        seating.MapPost("/holds", async (HoldRequest req, HttpContext ctx, HoldService holds, TimeProvider clock, CancellationToken ct) =>
        {
            var result = await holds.HoldAsync(req.EventId, ctx.User.UserId(), ctx.User.Email(), req.SeatIds, ct);
            return result.Outcome switch
            {
                HoldOutcome.Held => Results.Created($"/seating/holds/{result.Hold!.HoldId}", ToResponse(result.Hold, clock)),
                HoldOutcome.Conflict => Results.Problem(statusCode: 409, title: result.Error,
                    extensions: new Dictionary<string, object?> { ["conflicts"] = result.Conflicts }),
                HoldOutcome.UnknownEvent => Results.Problem(statusCode: 404, title: result.Error),
                _ => Results.Problem(statusCode: 400, title: result.Error,
                    extensions: new Dictionary<string, object?> { ["seats"] = result.Conflicts })
            };
        }).RequireAuthorization();

        seating.MapGet("/holds/{holdId:guid}", async (Guid holdId, HttpContext ctx, HoldStore store, TimeProvider clock) =>
        {
            var hold = await store.GetAsync(holdId);
            if (hold is null || (hold.BuyerId != ctx.User.UserId() && !ctx.User.IsPrivileged())) return Results.NotFound();
            return Results.Ok(ToResponse(hold, clock));
        }).RequireAuthorization();

        seating.MapDelete("/holds/{holdId:guid}", async (Guid holdId, HttpContext ctx, HoldStore store, HoldService holds, CancellationToken ct) =>
        {
            var hold = await store.GetAsync(holdId);
            if (hold is null || hold.BuyerId != ctx.User.UserId()) return Results.NotFound();
            if (hold.ConfirmedOrderId is not null) return Results.Problem(statusCode: 409, title: "Hold is already being paid for");
            await holds.ReleaseAsync(hold, "released-by-buyer", ct);
            return Results.NoContent();
        }).RequireAuthorization();

        return app;
    }

    private static HoldResponse ToResponse(Hold hold, TimeProvider clock) => new(
        hold.HoldId, hold.EventId, hold.Seats, hold.HeldAt, hold.ExpiresAt,
        Math.Max(0, (int)(hold.ExpiresAt - clock.GetUtcNow()).TotalSeconds));
}
