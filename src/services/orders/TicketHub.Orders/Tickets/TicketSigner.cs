using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using QRCoder;

namespace TicketHub.Orders.Tickets;

public sealed class TicketOptions
{
    public const string Section = "Tickets";

    public string SigningKey { get; set; } = "";
}

public sealed record ParsedTicketCode(Guid TicketId, Guid EventId);

/// <summary>
/// Code format: TH1.{ticketId:N}.{eventId:N}.{signature}. The signature is a truncated HMAC so the
/// QR stays small enough to scan from a cracked phone screen.
/// </summary>
public sealed class TicketSigner(IOptions<TicketOptions> options)
{
    private const string Prefix = "TH1";
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.SigningKey);

    public string CreateCode(Guid ticketId, Guid eventId)
    {
        var body = $"{Prefix}.{ticketId:N}.{eventId:N}";
        return $"{body}.{Sign(body)}";
    }

    public ParsedTicketCode? Verify(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var parts = code.Trim().Split('.');
        if (parts.Length != 4 || parts[0] != Prefix) return null;
        if (!Guid.TryParseExact(parts[1], "N", out var ticketId) || !Guid.TryParseExact(parts[2], "N", out var eventId)) return null;

        var expected = Encoding.ASCII.GetBytes(Sign($"{parts[0]}.{parts[1]}.{parts[2]}"));
        var actual = Encoding.ASCII.GetBytes(parts[3]);
        return CryptographicOperations.FixedTimeEquals(expected, actual) ? new ParsedTicketCode(ticketId, eventId) : null;
    }

    private string Sign(string body)
    {
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(body));
        return Convert.ToBase64String(hash, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string RenderQrPngBase64(string code)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        return Convert.ToBase64String(png);
    }
}
