namespace TicketHub.Orders.Payments;

public sealed class StripeOptions
{
    public const string Section = "Stripe";

    public string? SecretKey { get; set; }
    public string? PublishableKey { get; set; }
    public string? WebhookSecret { get; set; }

    // Test-mode keys only, and the .env.example placeholder doesn't count as configured.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SecretKey)
        && SecretKey.StartsWith("sk_test_", StringComparison.Ordinal)
        && !SecretKey.Contains("replace_me", StringComparison.Ordinal);
}

public sealed class MockPaymentOptions
{
    public const string Section = "MockPayments";

    public string WebhookSecret { get; set; } = "mock_whsec_local_only";
}
