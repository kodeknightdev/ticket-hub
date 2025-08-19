using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.Contracts;
using TicketHub.Orders.Data;
using TicketHub.Orders.Ordering;
using TicketHub.Orders.Payments;
using TicketHub.Orders.Sagas;

namespace TicketHub.Orders.Tests;

public class OrderFlowTests : IDisposable
{
    private readonly OrdersHarness _h = new();
    private readonly Guid _buyer = Guid.NewGuid();
    private readonly Guid _event = Guid.NewGuid();

    public void Dispose() => _h.Dispose();

    private PaymentNotification Notify(Order order, bool succeeded = true) =>
        new($"evt_{Guid.NewGuid():N}", succeeded ? PaymentNotificationKind.Succeeded : PaymentNotificationKind.Failed,
            order.PaymentRef!, order.Id, succeeded ? "payment.succeeded" : "payment.failed");

    private List<OutboxMessage> Outbox(string topic)
    {
        using var db = _h.Db.Create();
        return db.Set<OutboxMessage>().Where(m => m.Topic == topic).ToList();
    }

    private async Task<Order> CreateOrder(int seats = 2, string key = "key-1")
    {
        var hold = _h.Seating.AddHold(_buyer, _event, seats, _h.Clock.GetUtcNow());
        await using var db = _h.Db.Create();
        var result = await _h.Service(db).CreateAsync(hold.HoldId, _buyer, "buyer@tickethub.local", key);
        return result.Order!;
    }

    [Fact]
    public async Task Create_order_prices_seats_adds_fees_and_stages_order_created()
    {
        var order = await CreateOrder(seats: 2);

        order.Status.ShouldBe(OrderStatus.Pending);
        order.Subtotal.ShouldBe(100m);
        order.Fees.ShouldBe(Fees.For(100m, 2));
        order.Total.ShouldBe(order.Subtotal + order.Fees);
        order.PaymentRef.ShouldStartWith("mock_pi_");
        Outbox(Topics.OrderCreated).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_same_order()
    {
        var hold = _h.Seating.AddHold(_buyer, _event, 2, _h.Clock.GetUtcNow());
        await using var db1 = _h.Db.Create();
        var first = await _h.Service(db1).CreateAsync(hold.HoldId, _buyer, null, "same-key");
        await using var db2 = _h.Db.Create();
        var second = await _h.Service(db2).CreateAsync(hold.HoldId, _buyer, null, "same-key");

        second.Replayed.ShouldBeTrue();
        second.Order!.Id.ShouldBe(first.Order!.Id);
        second.Order.PaymentRef.ShouldBe(first.Order.PaymentRef);
        await using var check = _h.Db.Create();
        (await check.Orders.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Someone_elses_hold_is_rejected()
    {
        var hold = _h.Seating.AddHold(Guid.NewGuid(), _event, 1, _h.Clock.GetUtcNow());
        await using var db = _h.Db.Create();
        var result = await _h.Service(db).CreateAsync(hold.HoldId, _buyer, null, "k");
        result.Error.ShouldBe(CreateOrderError.NotYourHold);
    }

    [Fact]
    public async Task Successful_payment_confirms_hold_and_publishes_order_paid_once()
    {
        var order = await CreateOrder();
        var notification = Notify(order);

        await using (var db = _h.Db.Create()) await _h.Service(db).HandlePaymentAsync(notification);
        await using (var db = _h.Db.Create()) await _h.Service(db).HandlePaymentAsync(notification);

        await using var check = _h.Db.Create();
        (await check.Orders.SingleAsync()).Status.ShouldBe(OrderStatus.Paid);
        _h.Seating.Confirmed[order.HoldId].ShouldBe(order.Id);
        Outbox(Topics.OrderPaid).Count.ShouldBe(1);
        (await check.WebhookEvents.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Payment_after_hold_expired_is_refunded_automatically()
    {
        var order = await CreateOrder();
        _h.Seating.Expired.Add(order.HoldId);

        await using (var db = _h.Db.Create()) await _h.Service(db).HandlePaymentAsync(Notify(order));

        await using var check = _h.Db.Create();
        var saved = await check.Orders.SingleAsync();
        saved.Status.ShouldBe(OrderStatus.Refunded);
        saved.StatusReason.ShouldBe("hold-expired");
        _h.Payments.Refunds.ShouldHaveSingleItem().Amount.ShouldBe(order.Total);
        Outbox(Topics.OrderPaid).ShouldBeEmpty();
        var refunded = Outbox(Topics.OrderRefunded).ShouldHaveSingleItem();
        JsonSerializer.Deserialize<EventEnvelope>(refunded.Payload, Json.Options)!.As<OrderRefunded>().Reason.ShouldBe("hold-expired");
    }

    [Fact]
    public async Task Failed_payment_releases_the_hold()
    {
        var order = await CreateOrder();

        await using (var db = _h.Db.Create()) await _h.Service(db).HandlePaymentAsync(Notify(order, succeeded: false));

        await using var check = _h.Db.Create();
        (await check.Orders.SingleAsync()).Status.ShouldBe(OrderStatus.Failed);
        _h.Seating.Released.ShouldHaveSingleItem().Reason.ShouldBe("payment-failed");
        Outbox(Topics.OrderExpired).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Tickets_are_issued_one_per_seat_with_verifiable_codes()
    {
        var order = await CreateOrder(seats: 3);
        await using (var db = _h.Db.Create()) await _h.Service(db).HandlePaymentAsync(Notify(order));

        await using (var db = _h.Db.Create())
        {
            var paid = await db.Orders.Include(o => o.Lines).Include(o => o.Tickets).SingleAsync();
            _h.Service(db).IssueTickets(paid);
            await db.SaveChangesAsync();
        }

        await using var check = _h.Db.Create();
        var tickets = await check.Tickets.ToListAsync();
        tickets.Count.ShouldBe(3);
        tickets.ShouldAllBe(t => _h.Signer.Verify(t.Code) != null);
        (await check.Orders.SingleAsync()).Status.ShouldBe(OrderStatus.TicketsIssued);
        Outbox(Topics.TicketsIssued).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Ticket_can_only_be_checked_in_once()
    {
        var order = await CreateOrder(seats: 1);
        await using (var db = _h.Db.Create()) await _h.Service(db).HandlePaymentAsync(Notify(order));
        await using (var db = _h.Db.Create())
        {
            var paid = await db.Orders.Include(o => o.Lines).Include(o => o.Tickets).SingleAsync();
            _h.Service(db).IssueTickets(paid);
            await db.SaveChangesAsync();
        }

        string code;
        await using (var db = _h.Db.Create()) code = (await db.Tickets.SingleAsync()).Code;

        var staff = Guid.NewGuid();
        await using (var db = _h.Db.Create())
            (await new CheckInService(db, _h.Signer, _h.Clock).CheckInAsync(code, _event, staff)).Outcome.ShouldBe(CheckInOutcome.Admitted);
        await using (var db = _h.Db.Create())
            (await new CheckInService(db, _h.Signer, _h.Clock).CheckInAsync(code, _event, staff)).Outcome.ShouldBe(CheckInOutcome.AlreadyCheckedIn);
        await using (var db = _h.Db.Create())
            (await new CheckInService(db, _h.Signer, _h.Clock).CheckInAsync(code, Guid.NewGuid(), staff)).Outcome.ShouldBe(CheckInOutcome.WrongEvent);
        await using (var db = _h.Db.Create())
            (await new CheckInService(db, _h.Signer, _h.Clock).CheckInAsync("TH1.junk", null, staff)).Outcome.ShouldBe(CheckInOutcome.InvalidCode);

        Outbox(Topics.TicketCheckedIn).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_saga_refunds_voids_and_notifies_every_paid_order()
    {
        var orders = new List<Order>();
        for (var i = 0; i < 3; i++) orders.Add(await CreateOrder(seats: 2, key: $"k{i}"));
        foreach (var order in orders)
            await using (var db = _h.Db.Create()) await _h.Service(db).HandlePaymentAsync(Notify(order));
        var pending = await CreateOrder(seats: 1, key: "pending");

        var cancelled = new EventCancelled(_event, Guid.NewGuid(), "Test Gig", "Artist unwell", _h.Clock.GetUtcNow());
        await using (var db = _h.Db.Create())
        {
            await new CancellationSagaOrchestrator(db, _h.Payments, _h.Clock, NullLogger<CancellationSagaOrchestrator>.Instance).StartAsync(cancelled);
            await db.SaveChangesAsync();
        }
        await using (var db = _h.Db.Create())
            await new CancellationSagaOrchestrator(db, _h.Payments, _h.Clock, NullLogger<CancellationSagaOrchestrator>.Instance).RunPendingAsync();

        await using var check = _h.Db.Create();
        var saga = await check.CancellationSagas.Include(s => s.Steps).SingleAsync();
        saga.Status.ShouldBe(SagaStatus.Completed);
        saga.Steps.Count.ShouldBe(3);
        saga.Steps.ShouldAllBe(s => s.Stage == SagaStepStage.Completed);
        _h.Payments.Refunds.Count.ShouldBe(3);
        (await check.Orders.CountAsync(o => o.Status == OrderStatus.Refunded)).ShouldBe(3);
        (await check.Orders.SingleAsync(o => o.Id == pending.Id)).Status.ShouldBe(OrderStatus.Expired);
        Outbox(Topics.OrderRefunded).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Cancellation_saga_resumes_after_a_failure_without_refunding_twice()
    {
        var order = await CreateOrder(seats: 1);
        await using (var db = _h.Db.Create()) await _h.Service(db).HandlePaymentAsync(Notify(order));

        var cancelled = new EventCancelled(_event, Guid.NewGuid(), "Test Gig", "Venue flooded", _h.Clock.GetUtcNow());
        await using (var db = _h.Db.Create())
        {
            await new CancellationSagaOrchestrator(db, _h.Payments, _h.Clock, NullLogger<CancellationSagaOrchestrator>.Instance).StartAsync(cancelled);
            await db.SaveChangesAsync();
        }

        _h.Payments.FailRefunds = true;
        await using (var db = _h.Db.Create())
            await new CancellationSagaOrchestrator(db, _h.Payments, _h.Clock, NullLogger<CancellationSagaOrchestrator>.Instance).RunPendingAsync();
        await using (var db = _h.Db.Create())
        {
            var step = await db.CancellationSagaSteps.SingleAsync();
            step.Stage.ShouldBe(SagaStepStage.Pending);
            step.Attempts.ShouldBe(1);
        }

        _h.Payments.FailRefunds = false;
        await using (var db = _h.Db.Create())
            await new CancellationSagaOrchestrator(db, _h.Payments, _h.Clock, NullLogger<CancellationSagaOrchestrator>.Instance).RunPendingAsync();
        await using (var db = _h.Db.Create())
            await new CancellationSagaOrchestrator(db, _h.Payments, _h.Clock, NullLogger<CancellationSagaOrchestrator>.Instance).RunPendingAsync();

        _h.Payments.Refunds.Count.ShouldBe(1);
        await using var check = _h.Db.Create();
        (await check.CancellationSagas.SingleAsync()).Status.ShouldBe(SagaStatus.Completed);
    }
}
