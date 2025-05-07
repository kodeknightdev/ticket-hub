using Microsoft.EntityFrameworkCore;
using QRCoder;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.Notifier.Data;
using TicketHub.Notifier.Email;

namespace TicketHub.Notifier.Messaging;

/// <summary>Sends an email at most once per idempotency key, logging it in sent_emails.</summary>
public sealed class Mailer(NotifierDbContext db, IEmailSender sender, ILogger<Mailer> logger)
{
    public async Task<bool> SendOnceAsync(string idempotencyKey, string template, OutgoingEmail email, CancellationToken ct)
    {
        if (await db.SentEmails.AnyAsync(s => s.IdempotencyKey == idempotencyKey, ct)) return false;

        await sender.SendAsync(email, ct);
        db.SentEmails.Add(new SentEmail
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            Template = template,
            Recipient = email.To,
            Subject = email.Subject,
            SentAt = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsDuplicateKey())
        {
            // A replica sent it concurrently; one duplicate email is the accepted worst case.
            logger.LogInformation("Email {Key} already logged by another instance", idempotencyKey);
        }
        logger.LogInformation("Sent {Template} email to {Recipient}", template, email.To);
        return true;
    }

    public async Task<EventInfo?> EventAsync(Guid eventId, CancellationToken ct) =>
        await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId, ct);

    public static byte[] QrPng(string code)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(6);
    }
}
