using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TicketHub.BffKit;

/// <summary>Hands out a valid access token for the current session, refreshing it silently when close to expiry.</summary>
public sealed class TokenAccessor(ITokenStore store, IdentityClient identity, BffOptions options, TimeProvider clock, ILogger<TokenAccessor> logger)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RefreshLocks = new();
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromSeconds(60);

    public async Task<string?> GetAccessTokenAsync(HttpContext ctx, CancellationToken ct = default)
    {
        var sid = ctx.User.FindFirst(BffClaims.SessionId)?.Value;
        if (sid is null) return null;

        var tokens = await store.GetAsync(sid);
        if (tokens is null) return null;
        if (tokens.ExpiresAt - clock.GetUtcNow() > RefreshWindow) return tokens.AccessToken;

        // Parallel requests from the SPA would otherwise all try to rotate the same refresh token.
        var gate = RefreshLocks.GetOrAdd(sid, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            tokens = await store.GetAsync(sid);
            if (tokens is null) return null;
            if (tokens.ExpiresAt - clock.GetUtcNow() > RefreshWindow) return tokens.AccessToken;

            var refreshed = await identity.RefreshAsync(tokens.RefreshToken, ct);
            if (refreshed is null)
            {
                logger.LogInformation("Refresh failed for session, dropping it");
                await store.RemoveAsync(sid);
                return null;
            }

            var updated = new TokenSet(refreshed.AccessToken, refreshed.RefreshToken, refreshed.ExpiresAt, refreshed.User.Id);
            await store.SetAsync(sid, updated, TimeSpan.FromDays(7));
            return updated.AccessToken;
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1) RefreshLocks.TryRemove(sid, out _);
        }
    }
}

/// <summary>Attaches the session's bearer token to outgoing service calls.</summary>
public sealed class BffTokenHandler(IHttpContextAccessor accessor, TokenAccessor tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var ctx = accessor.HttpContext;
        if (ctx?.User.Identity?.IsAuthenticated == true && request.Headers.Authorization is null)
        {
            var token = await tokens.GetAccessTokenAsync(ctx, cancellationToken);
            if (token is not null) request.Headers.Authorization = new("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}

public static class BffClaims
{
    public const string SessionId = "sid";
}
