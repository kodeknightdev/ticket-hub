using Microsoft.Extensions.Options;
using Stripe;

namespace TicketHub.Orders.Payments;

public sealed class StripePaymentProvider(IOptions<StripeOptions> options, ILogger<StripePaymentProvider> logger) : IPaymentProvider
{
    private readonly StripeOptions _options = options.Value;
    private readonly StripeClient _client = new(options.Value.SecretKey);

    public string Name => "stripe";
    public string? PublishableKey => _options.PublishableKey;

    public async Task<PaymentSession> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var service = new PaymentIntentService(_client);
        var intent = await service.CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = ToMinorUnits(request.Amount),
            Currency = request.Currency.ToLowerInvariant(),
            ReceiptEmail = request.CustomerEmail,
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
            Metadata = new Dictionary<string, string> { ["orderId"] = request.OrderId.ToString() }
        }, new RequestOptions { IdempotencyKey = request.IdempotencyKey }, ct);

        return new PaymentSession(Name, intent.Id, intent.ClientSecret);
    }

    public async Task<RefundResult> RefundAsync(string paymentRef, decimal amount, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var refund = await new RefundService(_client).CreateAsync(new RefundCreateOptions
            {
                PaymentIntent = paymentRef,
                Amount = ToMinorUnits(amount)
            }, new RequestOptions { IdempotencyKey = idempotencyKey }, ct);
            return new RefundResult(refund.Status is "succeeded" or "pending", refund.Id);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe refund failed for {PaymentRef}", paymentRef);
            return new RefundResult(false, null, ex.StripeError?.Message ?? ex.Message);
        }
    }

    public PaymentNotification ParseWebhook(string payload, string? signatureHeader)
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _options.WebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            throw new WebhookSignatureException(ex.Message);
        }

        if (stripeEvent.Data.Object is not PaymentIntent intent)
            return new PaymentNotification(stripeEvent.Id, PaymentNotificationKind.Ignored, "", null, stripeEvent.Type);

        Guid? orderId = intent.Metadata.TryGetValue("orderId", out var raw) && Guid.TryParse(raw, out var id) ? id : null;
        var kind = stripeEvent.Type switch
        {
            "payment_intent.succeeded" => PaymentNotificationKind.Succeeded,
            "payment_intent.payment_failed" => PaymentNotificationKind.Failed,
            _ => PaymentNotificationKind.Ignored
        };
        return new PaymentNotification(stripeEvent.Id, kind, intent.Id, orderId, stripeEvent.Type, intent.LastPaymentError?.Message);
    }

    private static long ToMinorUnits(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
