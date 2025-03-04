using System.Text.Json;
using System.Text.Json.Serialization;

namespace TicketHub.Contracts;

/// <summary>
/// Wire format for every Kafka message. Python services read the same camelCase shape.
/// </summary>
public sealed record EventEnvelope
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }
    public int Version { get; init; } = 1;
    public required DateTimeOffset OccurredAt { get; init; }
    public required string Key { get; init; }
    public required JsonElement Data { get; init; }

    public T As<T>() => Data.Deserialize<T>(Json.Options)
        ?? throw new InvalidOperationException($"Empty payload for {Type}");

    public static EventEnvelope Create<T>(string type, string key, T payload, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Type = type,
        OccurredAt = DateTimeOffset.UtcNow,
        Key = key,
        Data = JsonSerializer.SerializeToElement(payload, Json.Options)
    };
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
