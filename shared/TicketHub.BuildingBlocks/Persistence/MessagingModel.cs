using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketHub.Contracts;

namespace TicketHub.BuildingBlocks.Persistence;

public static class MessagingModel
{
    public static ModelBuilder AddOutbox(this ModelBuilder model)
    {
        model.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Topic).HasMaxLength(200);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Type).HasMaxLength(200);
            e.Property(x => x.TraceParent).HasMaxLength(100);
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
        });
        return model;
    }

    public static ModelBuilder AddInbox(this ModelBuilder model)
    {
        model.Entity<ProcessedEvent>(e =>
        {
            e.ToTable("processed_events");
            e.HasKey(x => new { x.EventId, x.Consumer });
            e.Property(x => x.Consumer).HasMaxLength(100);
        });
        return model;
    }

    /// <summary>Stages an event in the outbox; it is published only if the surrounding SaveChanges commits.</summary>
    public static EventEnvelope Enqueue<T>(this DbContext db, string topic, string key, T payload)
    {
        var envelope = EventEnvelope.Create(topic, key, payload);
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = envelope.Id,
            Topic = topic,
            Key = key,
            Type = topic,
            Payload = JsonSerializer.Serialize(envelope, Json.Options),
            OccurredAt = envelope.OccurredAt,
            TraceParent = Activity.Current?.Id
        });
        return envelope;
    }

    public static async Task<bool> AlreadyProcessedAsync(this DbContext db, Guid eventId, string consumer, CancellationToken ct = default) =>
        await db.Set<ProcessedEvent>().AnyAsync(p => p.EventId == eventId && p.Consumer == consumer, ct);

    public static void MarkProcessed(this DbContext db, Guid eventId, string consumer) =>
        db.Set<ProcessedEvent>().Add(new ProcessedEvent { EventId = eventId, Consumer = consumer, ProcessedAt = DateTimeOffset.UtcNow });

    public static bool IsDuplicateKey(this DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase);
    }
}
