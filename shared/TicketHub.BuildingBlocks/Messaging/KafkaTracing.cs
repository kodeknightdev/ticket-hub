using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace TicketHub.BuildingBlocks.Messaging;

public static class KafkaTracing
{
    public const string SourceName = "TicketHub.Messaging";
    public static readonly ActivitySource Source = new(SourceName);
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public static void Inject(Activity? activity, Headers headers)
    {
        if (activity is null) return;
        Propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), headers,
            (h, key, value) => { h.Remove(key); h.Add(key, Encoding.UTF8.GetBytes(value)); });
    }

    public static PropagationContext Extract(Headers? headers) =>
        Propagator.Extract(default, headers, (h, key) =>
            h is not null && h.TryGetLastBytes(key, out var bytes) ? [Encoding.UTF8.GetString(bytes)] : []);

    public static ActivityContext ParseTraceParent(string? traceParent) =>
        traceParent is not null && ActivityContext.TryParse(traceParent, null, out var ctx) ? ctx : default;
}
