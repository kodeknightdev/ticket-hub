using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Security;
using TicketHub.Contracts;
using TicketHub.Orders.Data;
using TicketHub.Orders.Ordering;
using TicketHub.Orders.Payments;

namespace TicketHub.Orders.Endpoints;

public sealed record CreateOrderRequest(Guid HoldId);
public sealed record MockPayRequest(bool Succeed = true);
public sealed record RefundRequest(string? Reason);

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders").RequireAuthorization();

        orders.MapGet("/quote", async (Guid holdId, HttpContext ctx, OrderService service, CancellationToken ct) =>
        {
            var quote = await service.QuoteAsync(holdId, ctx.User.UserId(), ct);
            return quote is null ? Results.NotFound() : Results.Ok(quote);
        });

        orders.MapPost("/", async (CreateOrderRequest req, HttpContext ctx, OrderService service, IPaymentProvider payments, CancellationToken ct) =>
        {
            var key = ctx.Request.Headers[WellKnownHeaders.IdempotencyKey].ToString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
                return Results.Problem(statusCode: 400, title: "Idempotency-Key header is required");

            var result = await service.CreateAsync(req.HoldId, ctx.User.UserId(), ctx.User.Email(), key, ct);
            return result.Error switch
            {
                CreateOrderError.HoldNotFound => Results.Problem(statusCode: 410, title: "Hold has expired"),
                CreateOrderError.NotYourHold => Results.Problem(statusCode: 403, title: "Hold belongs to another buyer"),
                CreateOrderError.HoldAlreadyConfirmed => Results.Problem(statusCode: 409, title: "Hold is already paid for"),
                _ when result.Replayed => Results.Ok(result.Order!.ToDto(payments.PublishableKey, includeSecret: true)),
                _ => Results.Created($"/orders/{result.Order!.Id}", result.Order.ToDto(payments.PublishableKey, includeSecret: true))
            };
        });

        orders.MapGet("/mine", async (HttpContext ctx, OrdersDbContext db) =>
        {
            var buyerId = ctx.User.UserId();
            var list = await db.Orders.AsNoTracking().Include(o => o.Lines)
                .Where(o => o.BuyerId == buyerId).OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync();
            return Results.Ok(list.Select(o => o.ToDto()));
        });

        orders.MapGet("/{id:guid}", async (Guid id, HttpContext ctx, OrdersDbContext db, IPaymentProvider payments) =>
        {
            var order = await db.Orders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id);
            if (order is null || (order.BuyerId != ctx.User.UserId() && !ctx.User.IsPrivileged())) return Results.NotFound();
            return Results.Ok(order.ToDto(payments.PublishableKey, includeSecret: order.BuyerId == ctx.User.UserId()));
        });

        // Only wired up when no Stripe key is configured; runs the same signed-webhook path as a real provider.
        orders.MapPost("/{id:guid}/mock-pay", async (Guid id, MockPayRequest req, HttpContext ctx, OrdersDbContext db,
            IPaymentProvider payments, OrderService service, CancellationToken ct) =>
        {
            if (payments is not MockPaymentProvider mock) return Results.NotFound();
            var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && o.BuyerId == ctx.User.UserId(), ct);
            if (order is null) return Results.NotFound();

            var payload = JsonSerializer.Serialize(new MockWebhookPayload(
                $"mock_evt_{Guid.NewGuid():N}", req.Succeed ? "payment.succeeded" : "payment.failed", order.PaymentRef!, order.Id,
                req.Succeed ? null : "Card declined (mock)"), Json.Options);
            await service.HandlePaymentAsync(mock.ParseWebhook(payload, mock.Sign(payload)), ct);

            var updated = await db.Orders.AsNoTracking().Include(o => o.Lines).FirstAsync(o => o.Id == id, ct);
            return Results.Ok(updated.ToDto());
        });

        orders.MapPost("/{id:guid}/refund", async (Guid id, RefundRequest req, OrdersDbContext db, OrderService service, CancellationToken ct) =>
        {
            var order = await db.Orders.Include(o => o.Lines).Include(o => o.Tickets).FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order is null) return Results.NotFound();

            var result = await service.RefundAsync(order, req.Reason ?? "requested", $"refund-{order.Id:N}", ct);
            if (!result.Succeeded) return Results.Problem(statusCode: 409, title: result.Error);
            await db.SaveChangesAsync(ct);
            return Results.Ok(order.ToDto());
        }).RequireAuthorization("admin");

        app.MapGet("/cancellations", async (Guid? eventId, OrdersDbContext db) =>
        {
            var query = db.CancellationSagas.AsNoTracking().Include(s => s.Steps).AsQueryable();
            if (eventId is not null) query = query.Where(s => s.EventId == eventId);
            var sagas = await query.OrderByDescending(s => s.StartedAt).Take(50).ToListAsync();
            return Results.Ok(sagas.Select(s => new
            {
                s.Id,
                s.EventId,
                s.EventTitle,
                s.Reason,
                Status = s.Status.ToString(),
                s.StartedAt,
                s.CompletedAt,
                Total = s.Steps.Count,
                Completed = s.Steps.Count(x => x.Stage == SagaStepStage.Completed),
                Failed = s.Steps.Count(x => x.Stage == SagaStepStage.Failed),
                InProgress = s.Steps.Count(x => x.Stage is SagaStepStage.Pending or SagaStepStage.Refunded or SagaStepStage.TicketsVoided)
            }));
        }).WithTags("Cancellations");

        return app;
    }
}
