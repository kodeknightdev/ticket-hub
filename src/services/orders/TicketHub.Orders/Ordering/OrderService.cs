using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.Contracts;
using TicketHub.Orders.Clients;
using TicketHub.Orders.Data;
using TicketHub.Orders.Payments;
using TicketHub.Orders.Tickets;

namespace TicketHub.Orders.Ordering;

public enum CreateOrderError
{
    None,
    HoldNotFound,
    NotYourHold,
    HoldAlreadyConfirmed
}

public sealed record CreateOrderResult(Order? Order, bool Replayed, CreateOrderError Error = CreateOrderError.None);

public sealed class OrderService(
    OrdersDbContext db,
    ISeatingClient seating,
    IPricingClient pricing,
    IPaymentProvider payments,
    TicketSigner signer,
    TimeProvider clock,
    ILogger<OrderService> logger)
{
    public async Task<QuoteDto?> QuoteAsync(Guid holdId, Guid buyerId, CancellationToken ct = default)
    {
        var hold = await seating.GetHoldAsync(holdId, ct);
        if (hold is null || hold.BuyerId != buyerId) return null;

        var prices = await LockPricesAsync(hold, ct);
        var lines = hold.Seats.Select(s => new QuoteLine(s.SeatId, s.TierId, s.Label, prices[s.SeatId])).ToList();
        var subtotal = lines.Sum(l => l.Price);
        var fees = Fees.For(subtotal, lines.Count);

        var existing = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.HoldId == holdId && o.BuyerId == buyerId)
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct);

        return new QuoteDto(holdId, hold.EventId, lines, subtotal, fees, subtotal + fees, "GBP", existing?.ToDto(payments.PublishableKey, includeSecret: true));
    }

    public async Task<CreateOrderResult> CreateAsync(Guid holdId, Guid buyerId, string? buyerEmail, string idempotencyKey, CancellationToken ct = default)
    {
        var replay = await FindByKeyAsync(buyerId, idempotencyKey, ct);
        if (replay is not null) return new CreateOrderResult(replay, Replayed: true);

        var hold = await seating.GetHoldAsync(holdId, ct);
        if (hold is null) return new CreateOrderResult(null, false, CreateOrderError.HoldNotFound);
        if (hold.BuyerId != buyerId) return new CreateOrderResult(null, false, CreateOrderError.NotYourHold);
        if (hold.Confirmed) return new CreateOrderResult(null, false, CreateOrderError.HoldAlreadyConfirmed);

        var prices = await LockPricesAsync(hold, ct);
        var now = clock.GetUtcNow();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            BuyerEmail = buyerEmail,
            EventId = hold.EventId,
            HoldId = holdId,
            IdempotencyKey = idempotencyKey,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            ExpiresAt = hold.ExpiresAt,
            PaymentProvider = payments.Name,
            Lines = hold.Seats.Select(s => new OrderLine
            {
                Id = Guid.NewGuid(),
                SeatId = s.SeatId,
                TierId = s.TierId,
                SectionId = s.SectionId,
                Label = s.Label,
                Price = prices[s.SeatId]
            }).ToList()
        };
        order.Subtotal = order.Lines.Sum(l => l.Price);
        order.Fees = Fees.For(order.Subtotal, order.Lines.Count);
        order.Total = order.Subtotal + order.Fees;

        // Same key on the provider side, so a retried request can't create a second PaymentIntent.
        var session = await payments.CreatePaymentAsync(
            new PaymentRequest(order.Id, order.Total, order.Currency, $"order-{buyerId:N}-{idempotencyKey}", buyerEmail), ct);
        order.PaymentRef = session.PaymentRef;
        order.ClientSecret = session.ClientSecret;

        db.Orders.Add(order);
        db.Enqueue(Topics.OrderCreated, order.EventId.ToString(),
            new OrderCreated(order.Id, holdId, order.EventId, buyerId, order.Total, order.Currency, now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsDuplicateKey())
        {
            db.ChangeTracker.Clear();
            var winner = await FindByKeyAsync(buyerId, idempotencyKey, ct);
            if (winner is not null) return new CreateOrderResult(winner, Replayed: true);
            throw;
        }

        logger.LogInformation("Order {OrderId} created for hold {HoldId} ({Total} {Currency})", order.Id, holdId, order.Total, order.Currency);
        return new CreateOrderResult(order, Replayed: false);
    }

    public async Task HandlePaymentAsync(PaymentNotification notification, CancellationToken ct = default)
    {
        if (notification.Kind == PaymentNotificationKind.Ignored) return;
        if (await db.WebhookEvents.AnyAsync(w => w.Id == notification.EventId, ct)) return;

        var order = notification.OrderId is { } id
            ? await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct)
            : await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.PaymentRef == notification.PaymentRef, ct);

        db.WebhookEvents.Add(new PaymentWebhookEvent { Id = notification.EventId, Provider = payments.Name, Type = notification.Type, ReceivedAt = clock.GetUtcNow() });

        if (order is null)
        {
            logger.LogWarning("Payment notification {EventId} for unknown order", notification.EventId);
        }
        else if (notification.Kind == PaymentNotificationKind.Succeeded)
        {
            await OnPaymentSucceededAsync(order, ct);
        }
        else
        {
            await OnPaymentFailedAsync(order, notification.FailureReason, ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsDuplicateKey())
        {
            // Stripe retries webhooks; a concurrent delivery already handled this one.
            logger.LogInformation("Duplicate webhook {EventId} ignored", notification.EventId);
        }
    }

    private async Task OnPaymentSucceededAsync(Order order, CancellationToken ct)
    {
        if (order.Status is OrderStatus.Paid or OrderStatus.TicketsIssued or OrderStatus.Refunded) return;

        var (confirmed, reason) = await seating.ConfirmHoldAsync(order.HoldId, order.Id, ct);
        var now = clock.GetUtcNow();

        if (confirmed)
        {
            order.Status = OrderStatus.Paid;
            order.PaidAt = now;
            order.StatusReason = null;
            db.Enqueue(Topics.OrderPaid, order.EventId.ToString(), new OrderPaid(
                order.Id, order.HoldId, order.EventId, order.BuyerId,
                order.Lines.Select(l => new OrderLineInfo(l.SeatId, l.TierId, l.SectionId, l.Label, l.Price)).ToList(),
                order.Subtotal, order.Fees, order.Total, order.Currency, now));
            return;
        }

        // Money arrived after the seats were let go: compensate by refunding in full.
        logger.LogWarning("Payment for order {OrderId} arrived after hold was lost ({Reason}); refunding", order.Id, reason);
        var refund = await payments.RefundAsync(order.PaymentRef!, order.Total, $"late-{order.Id:N}", ct);
        if (!refund.Succeeded) throw new InvalidOperationException($"Refund failed for order {order.Id}: {refund.Error}");

        order.Status = OrderStatus.Refunded;
        order.StatusReason = "hold-expired";
        order.RefundedAt = now;
        db.Enqueue(Topics.OrderRefunded, order.EventId.ToString(),
            new OrderRefunded(order.Id, order.EventId, order.BuyerId, order.BuyerEmail, order.Total, 0, "hold-expired", now));
    }

    private async Task OnPaymentFailedAsync(Order order, string? reason, CancellationToken ct)
    {
        if (order.Status != OrderStatus.Pending) return;

        order.Status = OrderStatus.Failed;
        order.StatusReason = reason ?? "payment-failed";
        await seating.ReleaseHoldAsync(order.HoldId, "payment-failed", ct);
        db.Enqueue(Topics.OrderExpired, order.EventId.ToString(), new OrderExpired(order.Id, order.HoldId, order.EventId, "payment-failed"));
    }

    public async Task<bool> ExpireAsync(Order order, string reason, CancellationToken ct = default)
    {
        if (order.Status != OrderStatus.Pending) return false;
        order.Status = OrderStatus.Expired;
        order.StatusReason = reason;
        db.Enqueue(Topics.OrderExpired, order.EventId.ToString(), new OrderExpired(order.Id, order.HoldId, order.EventId, reason));
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task IssueTicketsAsync(Order order, CancellationToken ct = default)
    {
        if (order.Status != OrderStatus.Paid) return;
        var now = clock.GetUtcNow();

        foreach (var line in order.Lines)
        {
            var ticketId = Guid.NewGuid();
            // Added explicitly: a client-generated key on a navigation add would be treated as an update.
            var ticket = new Ticket
            {
                Id = ticketId,
                OrderId = order.Id,
                EventId = order.EventId,
                BuyerId = order.BuyerId,
                SeatId = line.SeatId,
                Label = line.Label,
                Code = signer.CreateCode(ticketId, order.EventId),
                Status = TicketStatus.Valid,
                IssuedAt = now
            };
            db.Tickets.Add(ticket);
            order.Tickets.Add(ticket);
        }
        order.Status = OrderStatus.TicketsIssued;

        db.Enqueue(Topics.TicketsIssued, order.EventId.ToString(), new TicketsIssued(
            order.Id, order.EventId, order.BuyerId, order.BuyerEmail,
            order.Tickets.Select(t => new IssuedTicket(t.Id, t.SeatId, t.Label, t.Code)).ToList(), now));
    }

    public async Task<RefundResult> RefundAsync(Order order, string reason, string idempotencyKey, CancellationToken ct = default)
    {
        if (order.Status is not (OrderStatus.Paid or OrderStatus.TicketsIssued))
            return new RefundResult(order.Status == OrderStatus.Refunded, null, $"Order is {order.Status}");

        var refund = await payments.RefundAsync(order.PaymentRef!, order.Total, idempotencyKey, ct);
        if (!refund.Succeeded) return refund;

        var now = clock.GetUtcNow();
        var ticketCount = order.Tickets.Count(t => t.Status != TicketStatus.Void);
        foreach (var ticket in order.Tickets) ticket.Status = TicketStatus.Void;
        order.Status = OrderStatus.Refunded;
        order.StatusReason = reason;
        order.RefundedAt = now;

        db.Enqueue(Topics.OrderRefunded, order.EventId.ToString(),
            new OrderRefunded(order.Id, order.EventId, order.BuyerId, order.BuyerEmail, order.Total, ticketCount, reason, now));
        return refund;
    }

    private async Task<Dictionary<Guid, decimal>> LockPricesAsync(HoldSnapshot hold, CancellationToken ct)
    {
        var locked = await pricing.LockPricesAsync(new LockRequest(
            hold.HoldId, hold.EventId, hold.HeldAt, hold.Seats.Select(s => new LockSeat(s.SeatId, s.TierId)).ToList()), ct);
        var prices = locked.ToDictionary(p => p.SeatId, p => p.Price);
        var missing = hold.Seats.Where(s => !prices.ContainsKey(s.SeatId)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"Pricing returned no price for {missing.Count} seat(s)");
        return prices;
    }

    private Task<Order?> FindByKeyAsync(Guid buyerId, string key, CancellationToken ct) =>
        db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.BuyerId == buyerId && o.IdempotencyKey == key, ct);
}
