using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketHub.Contracts;

namespace TicketHub.BuildingBlocks.Messaging;

public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(IOptions<KafkaOptions> options, ILogger<KafkaEventPublisher> logger)
    {
        _logger = logger;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            LingerMs = 5,
            MessageTimeoutMs = 15000
        }).Build();
    }

    public async Task PublishAsync(string topic, EventEnvelope envelope, string? traceParent = null, CancellationToken ct = default)
    {
        var parent = KafkaTracing.ParseTraceParent(traceParent);
        using var activity = KafkaTracing.Source.StartActivity($"{topic} publish", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("messaging.message.id", envelope.Id.ToString());

        var message = new Message<string, string>
        {
            Key = envelope.Key,
            Value = JsonSerializer.Serialize(envelope, Json.Options),
            Headers = new Confluent.Kafka.Headers()
        };
        KafkaTracing.Inject(activity ?? Activity.Current, message.Headers);

        await _producer.ProduceAsync(topic, message, ct);
        _logger.LogDebug("Published {Type} {Id} to {Topic}", envelope.Type, envelope.Id, topic);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

/// <summary>Used when Kafka:Enabled is false, e.g. in tests.</summary>
public sealed class NullEventPublisher : IEventPublisher
{
    public List<(string Topic, EventEnvelope Envelope)> Published { get; } = [];

    public Task PublishAsync(string topic, EventEnvelope envelope, string? traceParent = null, CancellationToken ct = default)
    {
        lock (Published) Published.Add((topic, envelope));
        return Task.CompletedTask;
    }
}
