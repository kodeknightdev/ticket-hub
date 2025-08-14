using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketHub.Contracts;
using TicketHub.TicketingBff.Clients;
using TicketHub.TestSupport;

namespace TicketHub.Bff.Tests;

public sealed class FakeSeatingReader : ISeatingReader
{
    public bool Fail { get; set; }
    public Dictionary<Guid, string> States { get; } = [];
    public HoldView? Hold { get; set; }

    public Task<Availability?> GetAvailabilityAsync(Guid eventId, CancellationToken ct)
    {
        if (Fail) throw new InvalidOperationException("seating down");
        return Task.FromResult<Availability?>(new Availability(States.Count, States.Values.Count(v => v == "available"),
            States.Values.Count(v => v == "held"), States.Values.Count(v => v == "sold"), States));
    }

    public Task<HoldView?> GetHoldAsync(Guid holdId, CancellationToken ct) => Task.FromResult(Hold?.HoldId == holdId ? Hold : null);
}

public sealed class TicketingFactory : BffFactory<TicketingBffApp>
{
    public FakeSeatingReader Seating { get; } = new();
    protected override IEnumerable<string> Downstreams => [EventsApi.Name, OrdersApi.Name, PricingApi.Name];

    protected override void ConfigureFakes(IServiceCollection services)
    {
        services.RemoveAll<ISeatingReader>();
        services.AddSingleton<ISeatingReader>(Seating);
    }
}

public sealed class TicketingBffTests : IDisposable
{
    private const string Xsrf = "XSRF-TOKEN-APP";
    private readonly TicketingFactory _factory = new();
    private readonly Guid _eventId = Guid.NewGuid();
    private readonly Guid _tier = Guid.NewGuid();
    private readonly Guid _seatA = Guid.NewGuid();
    private readonly Guid _seatB = Guid.NewGuid();

    public TicketingBffTests()
    {
        var evt = new
        {
            id = _eventId, organizerId = Guid.NewGuid(), title = "Neon Harbour Live", artist = "Neon Harbour", category = "Concert", city = "Manchester",
            venueId = Guid.NewGuid(), venueName = "Riverside Arena", startsAt = DateTimeOffset.UtcNow.AddDays(10), coverImageUrl = (string?)null,
            description = "x", status = "Published", capacity = 2, publishedAt = DateTimeOffset.UtcNow, tiers = Array.Empty<object>()
        };
        var layout = new
        {
            eventId = _eventId, capacity = 2,
            sections = new[] { new { id = Guid.NewGuid(), name = "Floor", sortOrder = 0, seats = new[] { new { id = _seatA, row = "A", number = 1, tierId = _tier }, new { id = _seatB, row = "A", number = 2, tierId = _tier } } } },
            tiers = new[] { new { id = _tier, name = "Standard", basePrice = 50m, minPrice = 40m, maxPrice = 90m, currentPrice = 50m } }
        };
        _factory.Stubs[EventsApi.Name] = new StubHandler()
            .OnGet($"/events/{_eventId}/layout", layout)
            .OnGet($"/events/{_eventId}", evt);
        _factory.Stubs[PricingApi.Name] = new StubHandler()
            .OnGet($"/prices/{_eventId}", new { eventId = _eventId.ToString(), tiers = new[] { new { tierId = _tier.ToString(), name = "Standard", price = 57.5m, basePrice = 50m } } });
        _factory.Seating.States[_seatA] = "sold";
        _factory.Seating.States[_seatB] = "available";
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Event_page_composes_catalogue_live_seats_and_prices()
    {
        var page = await _factory.Browser().GetFromJsonAsync<JsonElement>($"/bff/events/{_eventId}/page");

        page.GetProperty("partial").GetBoolean().ShouldBeFalse();
        page.GetProperty("event").GetProperty("title").GetString().ShouldBe("Neon Harbour Live");
        page.GetProperty("tiers")[0].GetProperty("price").GetDecimal().ShouldBe(57.5m);
        var seats = page.GetProperty("sections")[0].GetProperty("rows")[0].GetProperty("seats");
        seats[0].GetProperty("state").GetString().ShouldBe("sold");
        seats[1].GetProperty("state").GetString().ShouldBe("available");
        page.GetProperty("availability").GetProperty("sold").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Event_page_degrades_to_partial_when_seating_is_down()
    {
        _factory.Seating.Fail = true;

        var page = await _factory.Browser().GetFromJsonAsync<JsonElement>($"/bff/events/{_eventId}/page");

        page.GetProperty("partial").GetBoolean().ShouldBeTrue();
        page.GetProperty("failures")[0].GetString()!.ShouldStartWith("seating");
        page.GetProperty("sections")[0].GetProperty("rows")[0].GetProperty("seats")[0].GetProperty("state").GetString().ShouldBe("unknown");
        page.GetProperty("tiers")[0].GetProperty("price").GetDecimal().ShouldBe(57.5m);
    }

    [Fact]
    public async Task Unknown_event_is_404()
    {
        (await _factory.Browser().GetAsync($"/bff/events/{Guid.NewGuid()}/page")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Browser_never_receives_tokens()
    {
        var browser = _factory.Browser();
        var login = await browser.LoginAsync("buyer@tickethub.local", Xsrf);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        var user = await browser.GetAsync("/bff/user");
        foreach (var response in new[] { login, user })
        {
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldNotContain(IdentityStub.AccessTokenMarker);
            body.ShouldNotContain(IdentityStub.RefreshTokenMarker);
            body.ShouldNotContain("accessToken", Case.Insensitive);
            body.ShouldNotContain("refreshToken", Case.Insensitive);
            var cookies = response.Headers.TryGetValues("Set-Cookie", out var v) ? string.Join("\n", v) : "";
            cookies.ShouldNotContain(IdentityStub.AccessTokenMarker);
        }

        var me = JsonDocument.Parse(await user.Content.ReadAsStringAsync()).RootElement;
        me.GetProperty("isAuthenticated").GetBoolean().ShouldBeTrue();
        me.GetProperty("user").GetProperty("role").GetString().ShouldBe(Roles.Buyer);
    }

    [Fact]
    public async Task Session_cookie_is_httponly_secure_and_strict()
    {
        var login = await _factory.Browser().LoginAsync("buyer@tickethub.local", Xsrf);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-tickethub-app=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
    }

    [Fact]
    public async Task State_changing_calls_need_the_csrf_header_and_token()
    {
        var browser = _factory.Browser();
        await browser.LoginAsync("buyer@tickethub.local", Xsrf);

        var noHeader = await browser.PostAsJsonAsync("/bff/logout", new { });
        noHeader.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        request.Headers.Add("X-CSRF", "1");
        (await browser.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Checkout_for_someone_elses_hold_is_gone()
    {
        _factory.Seating.Hold = new HoldView(Guid.NewGuid(), _eventId, Guid.NewGuid(), [], DateTimeOffset.UtcNow.AddMinutes(5), false);
        var browser = _factory.Browser();
        await browser.LoginAsync("buyer@tickethub.local", Xsrf);

        (await browser.GetAsync($"/bff/checkout/{_factory.Seating.Hold.HoldId}")).StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await _factory.Browser().GetAsync($"/bff/checkout/{_factory.Seating.Hold.HoldId}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Checkout_aggregates_hold_locked_prices_and_quote_with_the_users_token()
    {
        var hold = new HoldView(Guid.NewGuid(), _eventId, DemoAccounts.Buyer, [new HeldSeatView(_seatB, _tier, "Floor A2")], DateTimeOffset.UtcNow.AddMinutes(8), false);
        _factory.Seating.Hold = hold;
        _factory.Stubs[PricingApi.Name].OnGet($"/prices/locks/{hold.HoldId}", new { holdId = hold.HoldId.ToString(), prices = new[] { new { seatId = _seatB.ToString(), tierId = _tier.ToString(), price = 55m } } });
        _factory.Stubs[OrdersApi.Name].OnGet("/orders/quote", new { holdId = hold.HoldId, eventId = _eventId, lines = new[] { new { seatId = _seatB, tierId = _tier, label = "Floor A2", price = 55m } }, subtotal = 55m, fees = 4.25m, total = 59.25m, currency = "GBP" });

        var browser = _factory.Browser();
        await browser.LoginAsync("buyer@tickethub.local", Xsrf);
        var page = await browser.GetFromJsonAsync<JsonElement>($"/bff/checkout/{hold.HoldId}");

        page.GetProperty("partial").GetBoolean().ShouldBeFalse();
        page.GetProperty("total").GetDecimal().ShouldBe(59.25m);
        page.GetProperty("eventTitle").GetString().ShouldBe("Neon Harbour Live");
        page.GetProperty("secondsLeft").GetInt32().ShouldBeGreaterThan(400);
        _factory.Stubs[OrdersApi.Name].Requests.Last().Headers.Authorization!.Parameter.ShouldBe(IdentityStub.AccessTokenMarker);
    }

}
