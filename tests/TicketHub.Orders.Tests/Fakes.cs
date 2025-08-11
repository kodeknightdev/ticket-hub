using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketHub.Orders.Clients;
using TicketHub.Orders.Data;
using TicketHub.Orders.Ordering;
using TicketHub.Orders.Payments;
using TicketHub.Orders.Tickets;
using TicketHub.TestSupport;

namespace TicketHub.Orders.Tests;

public sealed class FakeSeating : ISeatingClient
{
    public Dictionary<Guid, HoldSnapshot> Holds { get; } = [];
    public HashSet<Guid> Expired { get; } = [];
    public List<(Guid HoldId, string Reason)> Released { get; } = [];
    public Dictionary<Guid, Guid> Confirmed { get; } = [];

    public HoldSnapshot AddHold(Guid buyerId, Guid eventId, int seats, DateTimeOffset now)
    {
        var hold = new HoldSnapshot(Guid.NewGuid(), eventId, buyerId,
            Enumerable.Range(1, seats).Select(i => new HoldSeatSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), $"Floor A{i}")).ToList(),
            now, now.AddMinutes(10), false);
        Holds[hold.HoldId] = hold;
        return hold;
    }

    public Task<HoldSnapshot?> GetHoldAsync(Guid holdId, CancellationToken ct = default) =>
        Task.FromResult(Expired.Contains(holdId) ? null : Holds.GetValueOrDefault(holdId));

    public Task<(bool Confirmed, string Reason)> ConfirmHoldAsync(Guid holdId, Guid orderId, CancellationToken ct = default)
    {
        if (Expired.Contains(holdId) || !Holds.ContainsKey(holdId)) return Task.FromResult((false, "hold-expired"));
        Confirmed[holdId] = orderId;
        return Task.FromResult((true, ""));
    }

    public Task ReleaseHoldAsync(Guid holdId, string reason, CancellationToken ct = default)
    {
        Released.Add((holdId, reason));
        return Task.CompletedTask;
    }
}

public sealed class FakePricing(decimal price = 50m) : IPricingClient
{
    public Task<IReadOnlyList<LockedPrice>> LockPricesAsync(LockRequest request, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LockedPrice>>(request.Seats.Select(s => new LockedPrice(s.SeatId, s.TierId, price)).ToList());
}

public sealed class RecordingProvider(IPaymentProvider inner) : IPaymentProvider
{
    public List<(string Ref, decimal Amount, string Key)> Refunds { get; } = [];
    public bool FailRefunds { get; set; }

    public string Name => inner.Name;
    public string? PublishableKey => inner.PublishableKey;
    public Task<PaymentSession> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default) => inner.CreatePaymentAsync(request, ct);
    public PaymentNotification ParseWebhook(string payload, string? signatureHeader) => inner.ParseWebhook(payload, signatureHeader);

    public Task<RefundResult> RefundAsync(string paymentRef, decimal amount, string idempotencyKey, CancellationToken ct = default)
    {
        if (FailRefunds) return Task.FromResult(new RefundResult(false, null, "gateway down"));
        Refunds.Add((paymentRef, amount, idempotencyKey));
        return Task.FromResult(new RefundResult(true, "re_" + idempotencyKey));
    }
}

public sealed class OrdersHarness : IDisposable
{
    public SqliteDb<OrdersDbContext> Db { get; } = new(o => new OrdersDbContext(o));
    public FakeSeating Seating { get; } = new();
    public ManualClock Clock { get; } = new(new DateTimeOffset(2025, 5, 6, 10, 0, 0, TimeSpan.Zero));
    public MockPaymentProvider Mock { get; }
    public RecordingProvider Payments { get; }
    public TicketSigner Signer { get; } = new(Options.Create(new TicketOptions { SigningKey = "test-ticket-key-0123456789abcdef" }));

    public OrdersHarness()
    {
        Mock = new MockPaymentProvider(Options.Create(new MockPaymentOptions { WebhookSecret = "whsec_test_mock" }), Clock);
        Payments = new RecordingProvider(Mock);
    }

    public OrderService Service(OrdersDbContext db) =>
        new(db, Seating, new FakePricing(), Payments, Signer, Clock, NullLogger<OrderService>.Instance);

    public void Dispose() => Db.Dispose();
}
