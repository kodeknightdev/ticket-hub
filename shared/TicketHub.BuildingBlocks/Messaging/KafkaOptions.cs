namespace TicketHub.BuildingBlocks.Messaging;

public sealed class KafkaOptions
{
    public const string Section = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";
    public int MaxAttempts { get; set; } = 3;
    public bool Enabled { get; set; } = true;
}
