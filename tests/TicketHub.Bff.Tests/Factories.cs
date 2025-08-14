using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TicketHub.Contracts;
using TicketHub.TestSupport;

namespace TicketHub.Bff.Tests;

/// <summary>Plays the identity service: any demo email logs in, the role comes from the local part.</summary>
public static class IdentityStub
{
    public const string AccessTokenMarker = "eyJhbGciOiJIUzI1NiJ9.test-access";
    public const string RefreshTokenMarker = "refresh-token-secret-value";

    public static readonly Dictionary<string, (Guid Id, string Role)> Users = new()
    {
        ["buyer@tickethub.local"] = (DemoAccounts.Buyer, Roles.Buyer),
        ["organizer@tickethub.local"] = (DemoAccounts.Organizer, Roles.Organizer),
        ["organizer2@tickethub.local"] = (DemoAccounts.SecondOrganizer, Roles.Organizer),
        ["owner@tickethub.local"] = (DemoAccounts.Owner, Roles.Owner)
    };

    public static StubHandler Create() => new StubHandler()
        .OnPost("/auth/login", req =>
        {
            var body = req.Content!.ReadFromJsonAsync<JsonElement>().GetAwaiter().GetResult();
            var email = body.GetProperty("email").GetString()!;
            if (!Users.TryGetValue(email, out var user) || body.GetProperty("password").GetString() != "Demo@12345")
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return StubHandler.Json(new
            {
                accessToken = AccessTokenMarker,
                refreshToken = RefreshTokenMarker,
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                user = new { id = user.Id, email, displayName = email.Split('@')[0], role = user.Role }
            });
        })
        .OnPost("/auth/refresh", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized))
        .OnPost("/auth/revoke", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
}

public abstract class BffFactory<TEntry> : WebApplicationFactory<TEntry> where TEntry : class
{
    public Dictionary<string, StubHandler> Stubs { get; } = new() { ["identity"] = IdentityStub.Create() };

    protected abstract IEnumerable<string> Downstreams { get; }

    protected BffFactory()
    {
        foreach (var name in Downstreams) Stubs[name] = new StubHandler();
    }

    protected virtual void ConfigureFakes(IServiceCollection services) { }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Bff:TokenStore", "memory");

        builder.ConfigureTestServices(services =>
        {
            foreach (var (name, stub) in Stubs)
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => stub);
            ConfigureFakes(services);
        });
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });
}

public static class BrowserExtensions
{
    /// <summary>Does what the Angular app does: fetch /bff/user for the XSRF cookie, then log in with the header pair.</summary>
    public static async Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string xsrfCookie)
    {
        var user = await client.GetAsync("/bff/user");
        var token = ReadCookie(user, xsrfCookie);
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login") { Content = JsonContent.Create(new { email, password = "Demo@12345" }) };
        request.Headers.Add("X-CSRF", "1");
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    public static string? ReadCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0]).Where(v => v.StartsWith(name + "=", StringComparison.Ordinal)).Select(v => v[(name.Length + 1)..]).LastOrDefault()
            : null;
}
