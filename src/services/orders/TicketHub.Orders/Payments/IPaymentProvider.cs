namespace TicketHub.Orders.Payments;

public sealed record PaymentRequest(Guid OrderId, decimal Amount, string Currency, string IdempotencyKey, string? CustomerEmail);

public sealed record PaymentSession(string Provider, string PaymentRef, string? ClientSecret);

public enum PaymentNotificationKind
{
    Succeeded,
    Failed,
    Ignored
}

/// <summary>Provider-neutral view of a webhook; the rest of the service never sees Stripe types.</summary>
public sealed record PaymentNotification(string EventId, PaymentNotificationKind Kind, string PaymentRef, Guid? OrderId, string Type, string? FailureReason = null);

public sealed record RefundResult(bool Succeeded, string? RefundRef, string? Error = null);

public sealed class WebhookSignatureException(string message) : Exception(message);

/// <summary>Anti-corruption layer around the payment gateway.</summary>
public interface IPaymentProvider
{
    string Name { get; }
    string? PublishableKey { get; }

    Task<PaymentSession> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default);

    Task<RefundResult> RefundAsync(string paymentRef, decimal amount, string idempotencyKey, CancellationToken ct = default);

    /// <exception cref="WebhookSignatureException">When the signature does not verify.</exception>
    PaymentNotification ParseWebhook(string payload, string? signatureHeader);
}
