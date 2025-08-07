using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using TicketHub.Contracts;
using TicketHub.Identity.Auth;
using TicketHub.Identity.Data;
using TicketHub.TestSupport;

namespace TicketHub.Identity.Tests;

public sealed class IdentityApiTests : IDisposable
{
    private const string Password = "Demo@12345";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly WebApplicationFactory<Program> _factory;

    public IdentityApiTests()
    {
        _connection.Open();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789abcdef0123456789");
            b.UseSetting("SEED_DEMO_PASSWORD", Password);
            b.ConfigureServices(s => s.UseSqliteFor<IdentityDbContext>(_connection));
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Database.EnsureCreated();
        DemoSeeder.SeedAsync(db, scope.ServiceProvider).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _factory.Dispose();
        _connection.Dispose();
    }

    private async Task<TokenResponse> Login(HttpClient client, string email = "organizer@tickethub.local")
    {
        var response = await client.PostAsJsonAsync("/auth/login", new { email, password = Password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Json.Options))!;
    }

    [Fact]
    public async Task Demo_accounts_can_log_in_and_get_role_claims()
    {
        var tokens = await Login(_factory.CreateClient());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        jwt.Claims.First(c => c.Type == "role").Value.ShouldBe(Roles.Organizer);
        jwt.Claims.First(c => c.Type == "sub").Value.ShouldBe(DemoAccounts.Organizer.ToString());
        tokens.User.Email.ShouldBe("organizer@tickethub.local");
    }

    [Fact]
    public async Task Wrong_password_is_unauthorized()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/auth/login", new { email = "buyer@tickethub.local", password = "nope" });
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rotates_and_replay_revokes_the_family()
    {
        var client = _factory.CreateClient();
        var first = await Login(client);

        var rotated = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = first.RefreshToken });
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = (await rotated.Content.ReadFromJsonAsync<TokenResponse>(Json.Options))!;
        second.RefreshToken.ShouldNotBe(first.RefreshToken);

        (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = first.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = second.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_creates_a_buyer_and_validates_input()
    {
        var client = _factory.CreateClient();
        var bad = await client.PostAsJsonAsync("/auth/register", new { email = "not-an-email", password = "x", displayName = "" });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await client.PostAsJsonAsync("/auth/register", new { email = "new@tickethub.local", password = "Longenough1", displayName = "New Person" });
        ok.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ok.Content.ReadFromJsonAsync<TokenResponse>(Json.Options))!.User.Role.ShouldBe(Roles.Buyer);

        (await client.PostAsJsonAsync("/auth/register", new { email = "new@tickethub.local", password = "Longenough1", displayName = "Again" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Me_requires_a_token()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/users/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var tokens = await Login(client, "buyer@tickethub.local");
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        (await client.GetFromJsonAsync<UserDto>("/users/me", Json.Options))!.Role.ShouldBe(Roles.Buyer);
        (await client.GetAsync("/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
