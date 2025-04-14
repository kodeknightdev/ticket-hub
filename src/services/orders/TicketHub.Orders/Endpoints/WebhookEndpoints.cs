using TicketHub.Orders.Ordering;
using TicketHub.Orders.Payments;

namespace TicketHub.Orders.Endpoints;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var hooks = app.MapGroup("/webhooks").WithTags("Webhooks").AllowAnonymous();

        hooks.MapPost("/stripe", (HttpContext ctx, IPaymentProvider payments, OrderService service, ILoggerFactory logs, CancellationToken ct) =>
            Handle(ctx, payments, service, "Stripe-Signature", "stripe", logs, ct));

        hooks.MapPost("/mock", (HttpContext ctx, IPaymentProvider payments, OrderService service, ILoggerFactory logs, CancellationToken ct) =>
            Handle(ctx, payments, service, MockPaymentProvider.SignatureHeader, "mock", logs, ct));

        return app;
    }

    private static async Task<IResult> Handle(HttpContext ctx, IPaymentProvider payments, OrderService service, string header, string provider,
        ILoggerFactory logs, CancellationToken ct)
    {
        if (payments.Name != provider) return Results.NotFound();

        using var reader = new StreamReader(ctx.Request.Body);
        var payload = await reader.ReadToEndAsync(ct);

        PaymentNotification notification;
        try
        {
            notification = payments.ParseWebhook(payload, ctx.Request.Headers[header].ToString());
        }
        catch (WebhookSignatureException ex)
        {
            logs.CreateLogger("Webhooks").LogWarning("Rejected {Provider} webhook: {Reason}", provider, ex.Message);
            return Results.Problem(statusCode: 400, title: "Invalid signature");
        }

        await service.HandlePaymentAsync(notification, ct);
        return Results.Ok(new { received = true });
    }
}
