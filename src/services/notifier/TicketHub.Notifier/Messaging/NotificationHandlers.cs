using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.Contracts;
using TicketHub.Notifier.Data;
using TicketHub.Notifier.Email;
using TicketHub.Notifier.Templates;

namespace TicketHub.Notifier.Messaging;

public sealed class TicketsIssuedHandler(Mailer mailer, TemplateRenderer renderer) : IIntegrationEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var issued = envelope.As<TicketsIssued>();
        if (string.IsNullOrWhiteSpace(issued.BuyerEmail)) return;

        var evt = await mailer.EventAsync(issued.EventId, ct);
        var views = issued.Tickets.Select((t, i) => new TicketsIssuedEmail.TicketView(t.Label, t.Code, $"ticket{i}")).ToList();
        var html = await renderer.RenderAsync<TicketsIssuedEmail>(new Dictionary<string, object?>
        {
            [nameof(TicketsIssuedEmail.EventTitle)] = evt?.Title ?? "your event",
            [nameof(TicketsIssuedEmail.When)] = evt?.StartsAt,
            [nameof(TicketsIssuedEmail.Tickets)] = views
        });

        var images = views.Select(v => new InlineImage(v.ContentId, Mailer.QrPng(v.Code))).ToList();
        await mailer.SendOnceAsync($"tickets:{issued.OrderId}", "tickets-issued",
            new OutgoingEmail(issued.BuyerEmail, $"Your tickets for {evt?.Title ?? "your event"}", html, images), ct);
    }
}

public sealed class HoldExpiringHandler(Mailer mailer, TemplateRenderer renderer) : IIntegrationEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var expiring = envelope.As<HoldExpiring>();
        if (string.IsNullOrWhiteSpace(expiring.BuyerEmail)) return;

        var evt = await mailer.EventAsync(expiring.EventId, ct);
        var html = await renderer.RenderAsync<HoldExpiringEmail>(new Dictionary<string, object?>
        {
            [nameof(HoldExpiringEmail.EventTitle)] = evt?.Title ?? "your event",
            [nameof(HoldExpiringEmail.ExpiresAt)] = expiring.ExpiresAt
        });
        await mailer.SendOnceAsync($"hold-expiring:{expiring.HoldId}", "hold-expiring",
            new OutgoingEmail(expiring.BuyerEmail, "Your seat hold is about to expire", html), ct);
    }
}

public sealed class OrderRefundedHandler(Mailer mailer, TemplateRenderer renderer) : IIntegrationEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var refunded = envelope.As<OrderRefunded>();
        if (string.IsNullOrWhiteSpace(refunded.BuyerEmail)) return;

        var evt = await mailer.EventAsync(refunded.EventId, ct);
        var cancelled = refunded.Reason == "event-cancelled";
        var html = await renderer.RenderAsync<RefundEmail>(new Dictionary<string, object?>
        {
            [nameof(RefundEmail.EventTitle)] = evt?.Title ?? "your event",
            [nameof(RefundEmail.Amount)] = refunded.Amount,
            [nameof(RefundEmail.EventCancelled)] = cancelled,
            [nameof(RefundEmail.LatePayment)] = refunded.Reason == "hold-expired"
        });
        var subject = cancelled ? $"Cancelled: {evt?.Title ?? "your event"}" : "Your refund from ticket-hub";
        await mailer.SendOnceAsync($"refund:{refunded.OrderId}", cancelled ? "event-cancelled" : "refund",
            new OutgoingEmail(refunded.BuyerEmail, subject, html), ct);
    }
}

public sealed class EventInfoHandler(NotifierDbContext db) : IIntegrationEventHandler
{
    private const string Consumer = "notifier.event-info";

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (await db.AlreadyProcessedAsync(envelope.Id, Consumer, ct)) return;

        switch (envelope.Type)
        {
            case Topics.EventPublished:
            {
                var e = envelope.As<EventPublished>();
                var info = await db.Events.FirstOrDefaultAsync(x => x.EventId == e.EventId, ct);
                if (info is null)
                    db.Events.Add(new EventInfo { EventId = e.EventId, Title = e.Title, VenueName = e.VenueName, City = e.City, StartsAt = e.StartsAt });
                break;
            }
            case Topics.EventUpdated:
            {
                var e = envelope.As<EventUpdated>();
                var info = await db.Events.FirstOrDefaultAsync(x => x.EventId == e.EventId, ct);
                if (info is not null)
                {
                    info.Title = e.Title;
                    info.StartsAt = e.StartsAt;
                    info.City = e.City;
                }
                break;
            }
            case Topics.EventCancelled:
            {
                var e = envelope.As<EventCancelled>();
                var info = await db.Events.FirstOrDefaultAsync(x => x.EventId == e.EventId, ct);
                if (info is not null) info.Cancelled = true;
                break;
            }
        }

        db.MarkProcessed(envelope.Id, Consumer);
        await db.SaveChangesAsync(ct);
    }
}
