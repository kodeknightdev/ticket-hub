using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketHub.Contracts;
using TicketHub.Notifier.Data;
using TicketHub.Notifier.Email;
using TicketHub.Notifier.Messaging;
using TicketHub.Notifier.Templates;
using TicketHub.TestSupport;

namespace TicketHub.Notifier.Tests;

public sealed class CapturingSender : IEmailSender
{
    public List<OutgoingEmail> Sent { get; } = [];

    public Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        Sent.Add(email);
        return Task.CompletedTask;
    }
}

public sealed class NotifierTests : IDisposable
{
    private readonly SqliteDb<NotifierDbContext> _db = new(o => new NotifierDbContext(o));
    private readonly CapturingSender _sender = new();
    private readonly ServiceProvider _services = new ServiceCollection().AddLogging().BuildServiceProvider();

    public void Dispose()
    {
        _db.Dispose();
        _services.Dispose();
    }

    private TemplateRenderer Renderer => new(_services, NullLoggerFactory.Instance);

    [Fact]
    public async Task Tickets_email_lists_every_ticket_with_an_inline_qr()
    {
        var eventId = Guid.NewGuid();
        await using (var db = _db.Create())
        {
            db.Events.Add(new EventInfo { EventId = eventId, Title = "Neon Harbour Live", VenueName = "Riverside Arena", City = "Manchester", StartsAt = DateTimeOffset.UtcNow.AddDays(10) });
            await db.SaveChangesAsync();
        }

        var issued = new TicketsIssued(Guid.NewGuid(), eventId, Guid.NewGuid(), "buyer@tickethub.local",
            [new IssuedTicket(Guid.NewGuid(), Guid.NewGuid(), "Floor A A1", "TH1.code1"), new IssuedTicket(Guid.NewGuid(), Guid.NewGuid(), "Floor A A2", "TH1.code2")],
            DateTimeOffset.UtcNow);
        var envelope = EventEnvelope.Create(Topics.TicketsIssued, eventId.ToString(), issued);

        await using (var db = _db.Create())
            await new TicketsIssuedHandler(new Mailer(db, _sender, NullLogger<Mailer>.Instance), Renderer).HandleAsync(envelope, default);
        await using (var db = _db.Create())
            await new TicketsIssuedHandler(new Mailer(db, _sender, NullLogger<Mailer>.Instance), Renderer).HandleAsync(envelope, default);

        var email = _sender.Sent.ShouldHaveSingleItem();
        email.Subject.ShouldContain("Neon Harbour Live");
        email.Html.ShouldContain("Floor A A1");
        email.Html.ShouldContain("cid:ticket1");
        email.Images!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Refund_for_cancelled_event_uses_the_cancellation_template()
    {
        var refunded = new OrderRefunded(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "buyer@tickethub.local", 115.5m, 2, "event-cancelled", DateTimeOffset.UtcNow);
        await using var db = _db.Create();
        await new OrderRefundedHandler(new Mailer(db, _sender, NullLogger<Mailer>.Instance), Renderer)
            .HandleAsync(EventEnvelope.Create(Topics.OrderRefunded, "k", refunded), default);

        var email = _sender.Sent.ShouldHaveSingleItem();
        email.Subject.ShouldStartWith("Cancelled:");
        email.Html.ShouldContain("115.50");
    }

    [Fact]
    public async Task Hold_expiring_email_renders_expiry_time()
    {
        var html = await Renderer.RenderAsync<HoldExpiringEmail>(new Dictionary<string, object?>
        {
            [nameof(HoldExpiringEmail.EventTitle)] = "Velvet Static",
            [nameof(HoldExpiringEmail.ExpiresAt)] = new DateTimeOffset(2025, 5, 6, 18, 42, 0, TimeSpan.Zero)
        });
        html.ShouldContain("18:42");
        html.ShouldContain("Velvet Static");
    }
}
