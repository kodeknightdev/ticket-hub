using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TicketHub.Contracts;

namespace TicketHub.Orders.Payments;

public sealed record MockWebhookPayload(string Id, string Type, string PaymentRef, Guid OrderId, string? FailureReason = null);

/// <summary>
/// Local stand-in for Stripe. Webhooks are signed the same way (t=..,v1=HMAC) so the
/// verification path is exercised even without a Stripe account.
/// </summary>
public sealed class MockPaymentProvider(IOptions<MockPaymentOptions> options, TimeProvider clock) : IPaymentProvider
{
    public const string SignatureHeader = "Mock-Signature";
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public string Name => "mock";
    public string? PublishableKey => null;

    public Task<PaymentSession> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        // Deterministic from the idempotency key, like Stripe returning the same intent on retry.
        var reference = "mock_pi_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.IdempotencyKey)))[..24].ToLowerInvariant();
        return Task.FromResult(new PaymentSession(Name, reference, reference + "_secret"));
    }

    public Task<RefundResult> RefundAsync(string paymentRef, decimal amount, string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(new RefundResult(true, "mock_re_" + idempotencyKey));

    public PaymentNotification ParseWebhook(string payload, string? signatureHeader)
    {
        if (!Verify(payload, signatureHeader)) throw new WebhookSignatureException("Invalid mock signature");

        var body = JsonSerializer.Deserialize<MockWebhookPayload>(payload, Json.Options)
            ?? throw new WebhookSignatureException("Empty payload");
        var kind = body.Type switch
        {
            "payment.succeeded" => PaymentNotificationKind.Succeeded,
            "payment.failed" => PaymentNotificationKind.Failed,
            _ => PaymentNotificationKind.Ignored
        };
        return new PaymentNotification(body.Id, kind, body.PaymentRef, body.OrderId, body.Type, body.FailureReason);
    }

    public string Sign(string payload)
    {
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();
        return $"t={timestamp},v1={Compute(timestamp, payload)}";
    }

    private bool Verify(string payload, string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        var parts = header.Split(',').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        if (!parts.TryGetValue("t", out var t) || !long.TryParse(t, out var timestamp) || !parts.TryGetValue("v1", out var signature))
            return false;

        var age = clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if (age.Duration() > Tolerance) return false;

        var expected = Encoding.UTF8.GetBytes(Compute(timestamp, payload));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(signature));
    }

    private string Compute(long timestamp, string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.WebhookSecret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
