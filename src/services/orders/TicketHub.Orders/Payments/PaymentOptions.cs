namespace TicketHub.Orders.Payments;

public sealed class StripeOptions
{
    public const string Section = "Stripe";

    public string? SecretKey { get; set; }
    public string? PublishableKey { get; set; }
    public string? WebhookSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey) && SecretKey.StartsWith("sk_test_", StringComparison.Ordinal);
}

public sealed class MockPaymentOptions
{
    public const string Section = "MockPayments";

    public string WebhookSecret { get; set; } = "mock_whsec_local_only";
}
