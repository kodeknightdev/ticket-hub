using Confluent.Kafka;

namespace TicketHub.BuildingBlocks.Messaging;

public sealed class KafkaOptions
{
    public const string Section = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";
    public int MaxAttempts { get; set; } = 3;
    public bool Enabled { get; set; } = true;

    /// <summary>From Messaging:Provider. "kafka" (default) or "eventhubs" (Azure Event Hubs' Kafka endpoint).</summary>
    public string Provider { get; set; } = MessagingProviders.Kafka;

    /// <summary>From Messaging:EventHubs:ConnectionString; only used with the eventhubs provider.</summary>
    public string? EventHubsConnectionString { get; set; }

    /// <summary>Applies the broker address and, for Event Hubs, the SASL settings it requires.</summary>
    public T Apply<T>(T config) where T : ClientConfig
    {
        config.BootstrapServers = BootstrapServers;
        if (Provider == MessagingProviders.EventHubs)
        {
            // Event Hubs speaks the Kafka protocol on 9093 and authenticates with the namespace connection string.
            config.SecurityProtocol = SecurityProtocol.SaslSsl;
            config.SaslMechanism = SaslMechanism.Plain;
            config.SaslUsername = "$ConnectionString";
            config.SaslPassword = EventHubsConnectionString
                ?? throw new InvalidOperationException("Messaging:EventHubs:ConnectionString is required for the eventhubs provider");
        }
        return config;
    }
}

public static class MessagingProviders
{
    public const string Kafka = "kafka";
    public const string EventHubs = "eventhubs";
}
