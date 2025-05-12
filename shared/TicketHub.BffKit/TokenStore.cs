using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;
using TicketHub.Contracts;

namespace TicketHub.BffKit;

public sealed record TokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, Guid UserId);

/// <summary>Server-side home for the user's tokens. The browser only ever sees an opaque session cookie.</summary>
public interface ITokenStore
{
    Task<TokenSet?> GetAsync(string sessionId);
    Task SetAsync(string sessionId, TokenSet tokens, TimeSpan ttl);
    Task RemoveAsync(string sessionId);
}

public sealed class RedisTokenStore(IConnectionMultiplexer redis, BffOptions options) : ITokenStore
{
    private string Key(string sessionId) => $"bff:{options.AppName}:session:{sessionId}";

    public async Task<TokenSet?> GetAsync(string sessionId)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(sessionId));
        return value.HasValue ? JsonSerializer.Deserialize<TokenSet>(value.ToString(), Json.Options) : null;
    }

    public Task SetAsync(string sessionId, TokenSet tokens, TimeSpan ttl) =>
        redis.GetDatabase().StringSetAsync(Key(sessionId), JsonSerializer.Serialize(tokens, Json.Options), ttl);

    public Task RemoveAsync(string sessionId) => redis.GetDatabase().KeyDeleteAsync(Key(sessionId));
}

public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, TokenSet> _sessions = new();

    public Task<TokenSet?> GetAsync(string sessionId) => Task.FromResult(_sessions.TryGetValue(sessionId, out var t) ? t : null);

    public Task SetAsync(string sessionId, TokenSet tokens, TimeSpan ttl)
    {
        _sessions[sessionId] = tokens;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
