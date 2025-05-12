using System.Net;
using System.Net.Http.Json;
using TicketHub.Contracts;

namespace TicketHub.BffKit;

public sealed record IdentityUser(Guid Id, string Email, string DisplayName, string Role);
public sealed record IdentityTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, IdentityUser User);

public sealed class IdentityClient(HttpClient http)
{
    public const string Name = "identity";

    public async Task<IdentityTokens?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("/auth/login", new { email, password }, Json.Options, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IdentityTokens>(Json.Options, ct);
    }

    public async Task<IdentityTokens?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("/auth/refresh", new { refreshToken }, Json.Options, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IdentityTokens>(Json.Options, ct);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        using var _ = await http.PostAsJsonAsync("/auth/revoke", new { refreshToken }, Json.Options, ct);
    }
}
