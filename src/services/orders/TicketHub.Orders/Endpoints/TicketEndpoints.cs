using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Security;
using TicketHub.Orders.Data;
using TicketHub.Orders.Ordering;

namespace TicketHub.Orders.Endpoints;

public sealed record CheckInRequest(string Code, Guid? EventId);

public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var tickets = app.MapGroup("/tickets").WithTags("Tickets").RequireAuthorization();

        tickets.MapGet("/mine", async (HttpContext ctx, OrdersDbContext db) =>
        {
            var buyerId = ctx.User.UserId();
            var list = await db.Tickets.AsNoTracking().Where(t => t.BuyerId == buyerId).OrderByDescending(t => t.IssuedAt).Take(200).ToListAsync();
            return Results.Ok(list.Select(t => t.ToDto()));
        });

        tickets.MapGet("/{id:guid}", async (Guid id, HttpContext ctx, OrdersDbContext db) =>
        {
            var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (ticket is null || (ticket.BuyerId != ctx.User.UserId() && !ctx.User.IsPrivileged())) return Results.NotFound();
            return Results.Ok(ticket.ToDto(withQr: true));
        });

        app.MapPost("/checkin", async (CheckInRequest req, HttpContext ctx, CheckInService checkIn, CancellationToken ct) =>
        {
            var result = await checkIn.CheckInAsync(req.Code, req.EventId, ctx.User.UserId(), ct);
            return result.Outcome switch
            {
                CheckInOutcome.Admitted => Results.Ok(new { outcome = "admitted", ticket = result.Ticket }),
                CheckInOutcome.AlreadyCheckedIn => Results.Json(new { outcome = "already-checked-in", ticket = result.Ticket, checkedInAt = result.PreviouslyAt }, statusCode: 409),
                CheckInOutcome.Voided => Results.Json(new { outcome = "voided", ticket = result.Ticket }, statusCode: 409),
                CheckInOutcome.WrongEvent => Results.Json(new { outcome = "wrong-event" }, statusCode: 422),
                _ => Results.Json(new { outcome = "invalid" }, statusCode: 422)
            };
        }).RequireAuthorization("staff").WithTags("Check-in");

        return app;
    }
}
