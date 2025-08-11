using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketHub.Contracts;
using TicketHub.Orders.Payments;
using TicketHub.TestSupport;

namespace TicketHub.Orders.Tests;

public class WebhookSignatureTests
{
    private const string StripeSecret = "whsec_test_placeholder_secret";

    [Fact]
    public void Mock_webhook_with_valid_signature_parses()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var provider = new MockPaymentProvider(Options.Create(new MockPaymentOptions { WebhookSecret = "whsec_mock" }), clock);
        var orderId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new MockWebhookPayload("evt_1", "payment.succeeded", "mock_pi_1", orderId), Json.Options);

        var notification = provider.ParseWebhook(payload, provider.Sign(payload));

        notification.Kind.ShouldBe(PaymentNotificationKind.Succeeded);
        notification.OrderId.ShouldBe(orderId);
    }

    [Fact]
    public void Mock_webhook_with_bad_or_stale_signature_is_rejected()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var provider = new MockPaymentProvider(Options.Create(new MockPaymentOptions { WebhookSecret = "whsec_mock" }), clock);
        var payload = JsonSerializer.Serialize(new MockWebhookPayload("evt_1", "payment.succeeded", "mock_pi_1", Guid.NewGuid()), Json.Options);
        var signature = provider.Sign(payload);

        Should.Throw<WebhookSignatureException>(() => provider.ParseWebhook(payload.Replace("succeeded", "failed"), signature));
        Should.Throw<WebhookSignatureException>(() => provider.ParseWebhook(payload, null));

        clock.Advance(TimeSpan.FromMinutes(10));
        Should.Throw<WebhookSignatureException>(() => provider.ParseWebhook(payload, signature));
    }

    [Fact]
    public void Stripe_webhook_signature_is_verified()
    {
        var provider = new StripePaymentProvider(
            Options.Create(new StripeOptions { SecretKey = "sk_test_placeholder", WebhookSecret = StripeSecret }),
            NullLogger<StripePaymentProvider>.Instance);
        var orderId = Guid.NewGuid();
        var payload = StripeEvent("evt_test_1", "payment_intent.succeeded", "pi_test_1", orderId);

        var notification = provider.ParseWebhook(payload, StripeHeader(payload, StripeSecret));

        notification.Kind.ShouldBe(PaymentNotificationKind.Succeeded);
        notification.EventId.ShouldBe("evt_test_1");
        notification.PaymentRef.ShouldBe("pi_test_1");
        notification.OrderId.ShouldBe(orderId);
    }

    [Fact]
    public void Stripe_webhook_signed_with_wrong_secret_is_rejected()
    {
        var provider = new StripePaymentProvider(
            Options.Create(new StripeOptions { SecretKey = "sk_test_placeholder", WebhookSecret = StripeSecret }),
            NullLogger<StripePaymentProvider>.Instance);
        var payload = StripeEvent("evt_test_2", "payment_intent.succeeded", "pi_test_2", Guid.NewGuid());

        Should.Throw<WebhookSignatureException>(() => provider.ParseWebhook(payload, StripeHeader(payload, "whsec_wrong")));
    }

    // Same scheme Stripe documents for verifying webhooks by hand: v1 = HMAC-SHA256("{t}.{payload}").
    private static string StripeHeader(string payload, string secret)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{t}.{payload}"));
        return $"t={t},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string StripeEvent(string id, string type, string intentId, Guid orderId) => $$"""
        {
          "id": "{{id}}",
          "object": "event",
          "api_version": "2025-01-27.acacia",
          "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
          "type": "{{type}}",
          "livemode": false,
          "pending_webhooks": 1,
          "request": { "id": null, "idempotency_key": null },
          "data": {
            "object": {
              "id": "{{intentId}}",
              "object": "payment_intent",
              "amount": 11500,
              "currency": "gbp",
              "status": "succeeded",
              "livemode": false,
              "metadata": { "orderId": "{{orderId}}" }
            }
          }
        }
        """;
}
