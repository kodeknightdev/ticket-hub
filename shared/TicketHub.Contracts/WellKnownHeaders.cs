namespace TicketHub.Contracts;

public static class WellKnownHeaders
{
    public const string IdempotencyKey = "Idempotency-Key";
    public const string Csrf = "X-CSRF";
    public const string Cache = "X-Cache";
    public const string Attempt = "x-attempt";
    public const string OriginalTopic = "x-original-topic";
    public const string Error = "x-error";
}
