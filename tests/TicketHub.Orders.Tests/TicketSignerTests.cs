using Microsoft.Extensions.Options;
using TicketHub.Orders.Tickets;

namespace TicketHub.Orders.Tests;

public class TicketSignerTests
{
    private readonly TicketSigner _signer = new(Options.Create(new TicketOptions { SigningKey = "test-ticket-key-0123456789abcdef" }));

    [Fact]
    public void Signed_code_round_trips()
    {
        var ticketId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var parsed = _signer.Verify(_signer.CreateCode(ticketId, eventId));

        parsed.ShouldNotBeNull();
        parsed.TicketId.ShouldBe(ticketId);
        parsed.EventId.ShouldBe(eventId);
    }

    [Fact]
    public void Tampered_code_is_rejected()
    {
        var code = _signer.CreateCode(Guid.NewGuid(), Guid.NewGuid());
        var forged = code[..^2] + (code[^2] == 'A' ? "BB" : "AA");

        _signer.Verify(forged).ShouldBeNull();
    }

    [Fact]
    public void Code_signed_with_another_key_is_rejected()
    {
        var other = new TicketSigner(Options.Create(new TicketOptions { SigningKey = "some-other-key-0123456789abcdef!!" }));
        _signer.Verify(other.CreateCode(Guid.NewGuid(), Guid.NewGuid())).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("TH1.not-a-guid.x.y")]
    public void Garbage_is_rejected(string code) => _signer.Verify(code).ShouldBeNull();

    [Fact]
    public void Qr_renders_png()
    {
        var png = Convert.FromBase64String(TicketSigner.RenderQrPngBase64(_signer.CreateCode(Guid.NewGuid(), Guid.NewGuid())));
        png[..4].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }
}
