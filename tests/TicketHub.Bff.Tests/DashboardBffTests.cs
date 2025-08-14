using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketHub.Contracts;
using TicketHub.DashboardBff.Clients;
using TicketHub.TestSupport;

namespace TicketHub.Bff.Tests;

public sealed class FakeSeatingCounts : ISeatingCounts
{
    public Task<IReadOnlyDictionary<string, int>> SoldCountsAsync(IReadOnlyList<string> eventIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(eventIds.ToDictionary(id => id, _ => 42));
}

public sealed class DashboardFactory : BffFactory<DashboardBffApp>
{
    protected override IEnumerable<string> Downstreams => [AnalyticsApi.Name, EventsApi.Name, OrdersApi.Name, PricingApi.Name];

    protected override void ConfigureFakes(IServiceCollection services)
    {
        services.RemoveAll<ISeatingCounts>();
        services.AddSingleton<ISeatingCounts, FakeSeatingCounts>();
    }
}

public sealed class DashboardBffTests : IDisposable
{
    private const string Xsrf = "XSRF-TOKEN-DASH";
    private readonly DashboardFactory _factory = new();
    private readonly Guid _historic = Guid.NewGuid();
    private readonly Guid _draft = Guid.NewGuid();
    private readonly Guid _someoneElses = Guid.NewGuid();

    public DashboardBffTests()
    {
        var analytics = _factory.Stubs[AnalyticsApi.Name];
        analytics
            .OnGet("/analytics/events/ids", new[] { _historic })
            .OnGet("/analytics/kpis", new { from = "2025-07-01", to = "2025-07-30", kpis = new[] { new { key = "grossRevenue", value = 1000, previous = 800, change = 0.25 } }, trend = new { dates = Array.Empty<string>() } })
            .On(r => r.RequestUri!.AbsolutePath == "/analytics/events/performance", r =>
            {
                // echo back one row per requested event id, like the real service does
                var ids = (HttpUtility.ParseQueryString(r.RequestUri!.Query)["eventIds"] ?? $"{_historic},{_someoneElses}")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries);
                return StubHandler.Json(new { rows = ids.Select(id => new { eventId = id, title = id, sold = 10, capacity = 100, daysToEvent = 5 }) });
            });
        _factory.Stubs[EventsApi.Name]
            .OnGet("/events/ids", new[] { _draft })
            .OnGet("/events/stats", new { upcoming = 3, next30Days = 1, drafts = 0, cancelled = 0 });
    }

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> LoggedIn(string email)
    {
        var browser = _factory.Browser();
        (await browser.LoginAsync(email, Xsrf)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return browser;
    }

    private IEnumerable<string?> AnalyticsEventIds(string path) =>
        _factory.Stubs[AnalyticsApi.Name].Requests
            .Where(r => r.RequestUri!.AbsolutePath == path)
            .Select(r => HttpUtility.ParseQueryString(r.RequestUri!.Query)["eventIds"]);

    [Fact]
    public async Task Buyer_is_refused_at_login()
    {
        var response = await _factory.Browser().LoginAsync("buyer@tickethub.local", Xsrf);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? []).ShouldNotContain(c => c.StartsWith("__Host-tickethub-dash=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Anonymous_calls_are_unauthorized()
    {
        (await _factory.Browser().GetAsync("/bff/overview")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ticketing_session_cookie_is_not_accepted()
    {
        using var ticketing = new TicketingFactory();
        var login = await ticketing.Browser().LoginAsync("organizer@tickethub.local", "XSRF-TOKEN-APP");
        var appCookie = BrowserExtensions.ReadCookie(login, "__Host-tickethub-app");
        appCookie.ShouldNotBeNullOrEmpty();

        var client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        foreach (var cookie in new[] { $"__Host-tickethub-app={appCookie}", $"__Host-tickethub-dash={appCookie}" })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/bff/overview");
            request.Headers.Add("Cookie", cookie);
            (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            var user = new HttpRequestMessage(HttpMethod.Get, "/bff/user");
            user.Headers.Add("Cookie", cookie);
            var body = await (await client.SendAsync(user)).Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("isAuthenticated").GetBoolean().ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Organizer_queries_are_scoped_to_their_own_events()
    {
        var browser = await LoggedIn("organizer@tickethub.local");

        var all = await browser.GetFromJsonAsync<JsonElement>("/bff/events/performance");
        var asked = await browser.GetFromJsonAsync<JsonElement>($"/bff/events/performance?eventIds={_someoneElses}");

        var allowed = new[] { _historic.ToString(), _draft.ToString() };
        all.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("eventId").GetString()).ShouldBe(allowed, ignoreOrder: true);
        asked.GetProperty("rows").GetArrayLength().ShouldBe(0);
        AnalyticsEventIds("/analytics/events/performance").ShouldAllBe(ids => ids != null && !ids.Contains(_someoneElses.ToString()));

        (await browser.GetAsync($"/bff/events/{_someoneElses}/insights")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Owner_sees_everything_without_an_event_filter()
    {
        var browser = await LoggedIn("owner@tickethub.local");
        var rows = (await browser.GetFromJsonAsync<JsonElement>("/bff/events/performance")).GetProperty("rows");

        rows.GetArrayLength().ShouldBe(2);
        AnalyticsEventIds("/analytics/events/performance").ShouldAllBe(ids => ids == null);
        rows[0].GetProperty("liveSold").GetInt32().ShouldBe(42);
    }

    [Fact]
    public async Task Overview_is_cached_per_scope_and_filters()
    {
        var owner = await LoggedIn("owner@tickethub.local");

        var first = await owner.GetAsync("/bff/overview?from=2025-07-01&to=2025-07-30");
        var second = await owner.GetAsync("/bff/overview?from=2025-07-01&to=2025-07-30");
        var otherRange = await owner.GetAsync("/bff/overview?from=2025-06-01&to=2025-06-30");

        first.Headers.GetValues(WellKnownHeaders.Cache).Single().ShouldBe("MISS");
        second.Headers.GetValues(WellKnownHeaders.Cache).Single().ShouldBe("HIT");
        otherRange.Headers.GetValues(WellKnownHeaders.Cache).Single().ShouldBe("MISS");
        _factory.Stubs[AnalyticsApi.Name].Count("/analytics/kpis").ShouldBe(2);

        var organizer = await LoggedIn("organizer@tickethub.local");
        (await organizer.GetAsync("/bff/overview?from=2025-07-01&to=2025-07-30")).Headers.GetValues(WellKnownHeaders.Cache).Single().ShouldBe("MISS");
    }

    [Fact]
    public async Task Overview_marks_partial_when_events_is_down_and_skips_the_cache()
    {
        _factory.Stubs[EventsApi.Name].Fail("/events/stats", HttpStatusCode.BadRequest);
        var owner = await LoggedIn("owner@tickethub.local");

        var body = await owner.GetFromJsonAsync<JsonElement>("/bff/overview");
        body.GetProperty("partial").GetBoolean().ShouldBeTrue();
        body.GetProperty("kpis").GetArrayLength().ShouldBe(1);
        (await owner.GetAsync("/bff/overview")).Headers.GetValues(WellKnownHeaders.Cache).Single().ShouldBe("MISS");
    }

    [Fact]
    public async Task Csv_export_streams_rows_and_is_throttled()
    {
        var owner = await LoggedIn("owner@tickethub.local");

        var csv = await owner.GetAsync("/bff/export/performance.csv");
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var lines = (await csv.Content.ReadAsStringAsync()).Trim().Split('\n');
        lines[0].ShouldStartWith("eventId,title,sold");
        lines.Length.ShouldBe(3);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++) statuses.Add((await owner.GetAsync("/bff/export/performance.csv")).StatusCode);
        statuses.ShouldContain(HttpStatusCode.TooManyRequests);
    }
}
