using System.Text.Json;
using StackExchange.Redis;
using TicketHub.Contracts;

namespace TicketHub.Seating.Holds;

/// <summary>
/// All seat state transitions run as Lua scripts so a set of seats is held, confirmed or released
/// atomically. A seat key holds the owning hold id and carries the hold TTL.
/// </summary>
public sealed class HoldStore(IConnectionMultiplexer redis)
{
    public const string ExpiryIndex = "seating:holds:expiry";
    public const string WarnIndex = "seating:holds:warn";

    // KEYS: sold set, hold key, expiry zset, warn zset, event holds set, seat keys...
    // ARGV: hold id, ttl ms, hold json, expires-at ms, warn-at ms, seat ids...
    private const string HoldScript = """
        local conflicts = {}
        for i = 6, #KEYS do
          local seatId = ARGV[i]
          if redis.call('EXISTS', KEYS[i]) == 1 or redis.call('SISMEMBER', KEYS[1], seatId) == 1 then
            table.insert(conflicts, seatId)
          end
        end
        if #conflicts > 0 then return conflicts end
        for i = 6, #KEYS do
          redis.call('SET', KEYS[i], ARGV[1], 'PX', ARGV[2])
        end
        redis.call('SET', KEYS[2], ARGV[3], 'PX', tonumber(ARGV[2]) + 120000)
        redis.call('ZADD', KEYS[3], ARGV[4], ARGV[1])
        redis.call('ZADD', KEYS[4], ARGV[5], ARGV[1])
        redis.call('SADD', KEYS[5], ARGV[1])
        return {}
        """;

    // KEYS: hold key, expiry zset, warn zset, event holds set, seat keys...  ARGV: hold id
    private const string ReleaseScript = """
        local released = 0
        for i = 5, #KEYS do
          if redis.call('GET', KEYS[i]) == ARGV[1] then
            redis.call('DEL', KEYS[i])
            released = released + 1
          end
        end
        redis.call('DEL', KEYS[1])
        redis.call('ZREM', KEYS[2], ARGV[1])
        redis.call('ZREM', KEYS[3], ARGV[1])
        redis.call('SREM', KEYS[4], ARGV[1])
        return released
        """;

    // KEYS: hold key, expiry zset, warn zset, seat keys...  ARGV: hold id, hold json
    private const string ConfirmScript = """
        for i = 4, #KEYS do
          if redis.call('GET', KEYS[i]) ~= ARGV[1] then return 0 end
        end
        for i = 4, #KEYS do redis.call('PERSIST', KEYS[i]) end
        redis.call('SET', KEYS[1], ARGV[2])
        redis.call('ZREM', KEYS[2], ARGV[1])
        redis.call('ZREM', KEYS[3], ARGV[1])
        return 1
        """;

    // KEYS: sold set, hold key, event holds set, seat keys...  ARGV: hold id, seat ids...
    private const string SellScript = """
        for i = 4, #KEYS do
          redis.call('SADD', KEYS[1], ARGV[i - 2])
          if redis.call('GET', KEYS[i]) == ARGV[1] then redis.call('DEL', KEYS[i]) end
        end
        redis.call('DEL', KEYS[2])
        redis.call('SREM', KEYS[3], ARGV[1])
        return 1
        """;

    private IDatabase Db => redis.GetDatabase();

    public static string SeatKey(Guid eventId, Guid seatId) => $"seating:seat:{eventId}:{seatId}";
    public static string SoldKey(Guid eventId) => $"seating:sold:{eventId}";
    public static string SoldLoadedKey(Guid eventId) => $"seating:sold-loaded:{eventId}";
    public static string HoldKey(Guid holdId) => $"seating:hold:{holdId}";
    public static string EventHoldsKey(Guid eventId) => $"seating:event-holds:{eventId}";

    public async Task<IReadOnlyList<Guid>> TryHoldAsync(Hold hold, TimeSpan ttl, TimeSpan warnBefore)
    {
        var seatIds = hold.Seats.Select(s => s.SeatId).ToArray();
        RedisKey[] keys =
        [
            SoldKey(hold.EventId), HoldKey(hold.HoldId), ExpiryIndex, WarnIndex, EventHoldsKey(hold.EventId),
            .. seatIds.Select(id => (RedisKey)SeatKey(hold.EventId, id))
        ];
        RedisValue[] args =
        [
            hold.HoldId.ToString(),
            (long)ttl.TotalMilliseconds,
            JsonSerializer.Serialize(hold, Json.Options),
            hold.ExpiresAt.ToUnixTimeMilliseconds(),
            hold.ExpiresAt.Subtract(warnBefore).ToUnixTimeMilliseconds(),
            .. seatIds.Select(id => (RedisValue)id.ToString())
        ];

        var result = (RedisResult[]?)await Db.ScriptEvaluateAsync(HoldScript, keys, args) ?? [];
        return result.Select(r => Guid.Parse(r.ToString()!)).ToList();
    }

    public async Task<Hold?> GetAsync(Guid holdId)
    {
        var json = await Db.StringGetAsync(HoldKey(holdId));
        return json.HasValue ? JsonSerializer.Deserialize<Hold>(json.ToString(), Json.Options) : null;
    }

    public async Task<int> ReleaseAsync(Hold hold)
    {
        RedisKey[] keys =
        [
            HoldKey(hold.HoldId), ExpiryIndex, WarnIndex, EventHoldsKey(hold.EventId),
            .. hold.Seats.Select(s => (RedisKey)SeatKey(hold.EventId, s.SeatId))
        ];
        var result = await Db.ScriptEvaluateAsync(ReleaseScript, keys, [hold.HoldId.ToString()]);
        return (int)result;
    }

    public async Task<bool> ConfirmAsync(Hold hold, Guid orderId)
    {
        var confirmed = hold with { ConfirmedOrderId = orderId };
        RedisKey[] keys =
        [
            HoldKey(hold.HoldId), ExpiryIndex, WarnIndex,
            .. hold.Seats.Select(s => (RedisKey)SeatKey(hold.EventId, s.SeatId))
        ];
        var result = await Db.ScriptEvaluateAsync(ConfirmScript, keys, [hold.HoldId.ToString(), JsonSerializer.Serialize(confirmed, Json.Options)]);
        return (int)result == 1;
    }

    public async Task MarkSoldAsync(Guid eventId, Guid holdId, IReadOnlyList<Guid> seatIds)
    {
        RedisKey[] keys =
        [
            SoldKey(eventId), HoldKey(holdId), EventHoldsKey(eventId),
            .. seatIds.Select(id => (RedisKey)SeatKey(eventId, id))
        ];
        RedisValue[] args = [holdId.ToString(), .. seatIds.Select(id => (RedisValue)id.ToString())];
        await Db.ScriptEvaluateAsync(SellScript, keys, args);
    }

    public async Task<bool> ClaimExpiredAsync(string index, Guid holdId) => await Db.SortedSetRemoveAsync(index, holdId.ToString());

    public async Task<IReadOnlyList<Guid>> DueAsync(string index, DateTimeOffset now, int take = 100)
    {
        var due = await Db.SortedSetRangeByScoreAsync(index, double.NegativeInfinity, now.ToUnixTimeMilliseconds(), take: take);
        return due.Select(v => Guid.Parse(v.ToString())).ToList();
    }

    public async Task<IReadOnlyList<Guid>> HoldsForEventAsync(Guid eventId) =>
        (await Db.SetMembersAsync(EventHoldsKey(eventId))).Select(v => Guid.Parse(v.ToString())).ToList();

    public async Task<Dictionary<Guid, string>> SeatStatesAsync(Guid eventId, IReadOnlyList<Guid> seatIds)
    {
        var db = Db;
        var heldTask = db.StringGetAsync(seatIds.Select(id => (RedisKey)SeatKey(eventId, id)).ToArray());
        var soldTask = db.SetMembersAsync(SoldKey(eventId));
        await Task.WhenAll(heldTask, soldTask);

        var sold = soldTask.Result.Select(v => v.ToString()).ToHashSet();
        var held = heldTask.Result;
        var states = new Dictionary<Guid, string>(seatIds.Count);
        for (var i = 0; i < seatIds.Count; i++)
        {
            var id = seatIds[i];
            states[id] = sold.Contains(id.ToString()) ? SeatStates.Sold : held[i].HasValue ? SeatStates.Held : SeatStates.Available;
        }
        return states;
    }

    public async Task<bool> IsSoldSetLoadedAsync(Guid eventId) => await Db.KeyExistsAsync(SoldLoadedKey(eventId));

    public async Task LoadSoldSetAsync(Guid eventId, IReadOnlyList<Guid> soldSeatIds)
    {
        var db = Db;
        if (soldSeatIds.Count > 0)
            await db.SetAddAsync(SoldKey(eventId), soldSeatIds.Select(id => (RedisValue)id.ToString()).ToArray());
        await db.StringSetAsync(SoldLoadedKey(eventId), "1", TimeSpan.FromHours(12));
    }

    public async Task<long> SoldCountAsync(Guid eventId) => await Db.SetLengthAsync(SoldKey(eventId));
}

public static class SeatStates
{
    public const string Available = "available";
    public const string Held = "held";
    public const string Sold = "sold";
}
