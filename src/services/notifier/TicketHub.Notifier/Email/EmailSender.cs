using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace TicketHub.Notifier.Email;

public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string From { get; set; } = "tickets@tickethub.local";
    public string FromName { get; set; } = "ticket-hub";
}

public sealed record InlineImage(string ContentId, byte[] Png);

public sealed record OutgoingEmail(string To, string Subject, string Html, IReadOnlyList<InlineImage>? Images = null);

public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken ct = default);
}

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.From));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = email.Subject;

        var body = new BodyBuilder { HtmlBody = email.Html };
        foreach (var image in email.Images ?? [])
        {
            var resource = body.LinkedResources.Add($"{image.ContentId}.png", image.Png, new ContentType("image", "png"));
            resource.ContentId = image.ContentId;
        }
        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(o.Host, o.Port, SecureSocketOptions.None, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
