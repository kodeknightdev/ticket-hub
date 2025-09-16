using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketHub.Contracts;

namespace TicketHub.BuildingBlocks.Messaging;

public sealed class ConsumerRegistration
{
    public required string GroupId { get; init; }
    public Dictionary<string, Type> Handlers { get; } = new(StringComparer.Ordinal);

    public ConsumerRegistration On<THandler>(params string[] topics) where THandler : IIntegrationEventHandler
    {
        foreach (var topic in topics) Handlers[topic] = typeof(THandler);
        return this;
    }
}

/// <summary>
/// One consumer group per service. Failed messages go to the group's retry topic and,
/// after MaxAttempts, to its dead letter topic so a poison message never blocks a partition.
/// </summary>
public sealed class KafkaConsumerService(
    ConsumerRegistration registration,
    IServiceScopeFactory scopes,
    IEventPublisher publisher,
    IOptions<KafkaOptions> options,
    ILogger<KafkaConsumerService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => Run(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task Run(CancellationToken ct)
    {
        var retryTopic = Topics.RetryFor(registration.GroupId);
        var config = options.Value.Apply(new ConsumerConfig
        {
            GroupId = registration.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true
        });

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => logger.LogWarning("Kafka error: {Reason}", e.Reason))
            .Build();

        consumer.Subscribe(registration.Handlers.Keys.Append(retryTopic));
        logger.LogInformation("Consumer {Group} subscribed to {Topics}", registration.GroupId, string.Join(", ", registration.Handlers.Keys));

        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                logger.LogWarning(ex, "Consume failed");
                await Task.Delay(1000, ct).ConfigureAwait(false);
                continue;
            }

            if (result?.Message is null) continue;

            await ProcessAsync(result, retryTopic, ct);
            consumer.Commit(result);
        }

        consumer.Close();
    }

    private async Task ProcessAsync(ConsumeResult<string, string> result, string retryTopic, CancellationToken ct)
    {
        Confluent.Kafka.Headers headers = result.Message.Headers;
        var topic = result.Topic == retryTopic ? ReadHeader(headers, WellKnownHeaders.OriginalTopic) ?? result.Topic : result.Topic;
        var attempt = int.TryParse(ReadHeader(headers, WellKnownHeaders.Attempt), out var a) ? a : 1;

        EventEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope>(result.Message.Value, Json.Options);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Unreadable message on {Topic}, sending to DLQ", topic);
            await DeadLetter(result, topic, attempt, ex.Message, ct);
            return;
        }

        if (envelope is null || !registration.Handlers.TryGetValue(topic, out var handlerType)) return;

        var parent = KafkaTracing.Extract(headers);
        using var activity = KafkaTracing.Source.StartActivity($"{topic} process", ActivityKind.Consumer, parent.ActivityContext);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.consumer.group.name", registration.GroupId);
        activity?.SetTag("messaging.message.id", envelope.Id.ToString());

        if (attempt > 1) await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);

        try
        {
            using var scope = scopes.CreateScope();
            var handler = (IIntegrationEventHandler)scope.ServiceProvider.GetRequiredService(handlerType);
            await handler.HandleAsync(envelope, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (attempt >= options.Value.MaxAttempts)
            {
                logger.LogError(ex, "Giving up on {Type} {Id} after {Attempt} attempts", envelope.Type, envelope.Id, attempt);
                await DeadLetter(result, topic, attempt, ex.Message, ct);
            }
            else
            {
                logger.LogWarning(ex, "Handler failed for {Type} {Id}, scheduling retry {Next}", envelope.Type, envelope.Id, attempt + 1);
                await Forward(retryTopic, result, topic, attempt + 1, ex.Message, ct);
            }
        }
    }

    private Task DeadLetter(ConsumeResult<string, string> result, string topic, int attempt, string error, CancellationToken ct) =>
        Forward(Topics.DeadLetterFor(registration.GroupId), result, topic, attempt, error, ct);

    private async Task Forward(string target, ConsumeResult<string, string> result, string originalTopic, int attempt, string error, CancellationToken ct)
    {
        if (publisher is not KafkaEventPublisher)
        {
            logger.LogWarning("No Kafka producer available, dropping message for {Target}", target);
            return;
        }

        using var producer = new ProducerBuilder<string, string>(options.Value.Apply(new ProducerConfig())).Build();
        var headers = new Confluent.Kafka.Headers
        {
            { WellKnownHeaders.OriginalTopic, Encoding.UTF8.GetBytes(originalTopic) },
            { WellKnownHeaders.Attempt, Encoding.UTF8.GetBytes(attempt.ToString()) },
            { WellKnownHeaders.Error, Encoding.UTF8.GetBytes(error.Length > 500 ? error[..500] : error) }
        };
        foreach (var h in result.Message.Headers.Where(h => h.Key is "traceparent" or "tracestate"))
            headers.Add(h.Key, h.GetValueBytes());

        await producer.ProduceAsync(target, new Message<string, string> { Key = result.Message.Key, Value = result.Message.Value, Headers = headers }, ct);
    }

    private static string? ReadHeader(Confluent.Kafka.Headers? headers, string key) =>
        headers is not null && headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
}
