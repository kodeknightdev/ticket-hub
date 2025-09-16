using Confluent.Kafka;
using Microsoft.AspNetCore.Builder;
using TicketHub.BuildingBlocks.Hosting;
using TicketHub.BuildingBlocks.Messaging;

namespace TicketHub.Events.Tests;

public sealed class ProviderConfigTests
{
    [Fact]
    public void Kafka_provider_only_sets_the_broker()
    {
        var config = new KafkaOptions { BootstrapServers = "kafka:9092" }.Apply(new ProducerConfig());

        config.BootstrapServers.ShouldBe("kafka:9092");
        config.SecurityProtocol.ShouldBeNull();
    }

    [Fact]
    public void Event_hubs_provider_uses_sasl_with_the_connection_string()
    {
        var options = new KafkaOptions
        {
            BootstrapServers = "placeholder.servicebus.windows.net:9093",
            Provider = MessagingProviders.EventHubs,
            EventHubsConnectionString = "Endpoint=sb://placeholder.servicebus.windows.net/;SharedAccessKey=x"
        };

        var config = options.Apply(new ConsumerConfig());

        config.SecurityProtocol.ShouldBe(SecurityProtocol.SaslSsl);
        config.SaslUsername.ShouldBe("$ConnectionString");
        Should.Throw<InvalidOperationException>(() => new KafkaOptions { Provider = MessagingProviders.EventHubs }.Apply(new ProducerConfig()));
    }

    [Fact]
    public void Key_vault_provider_reads_csi_mounted_files()
    {
        var dir = Directory.CreateTempSubdirectory("kv-");
        File.WriteAllText(Path.Combine(dir.FullName, "Jwt__SigningKey"), "from-key-vault");
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Secrets:Provider"] = "keyvault";
        builder.Configuration["Secrets:Path"] = dir.FullName;

        builder.AddSecrets();

        builder.Configuration["Jwt:SigningKey"].ShouldBe("from-key-vault");
    }

    [Fact]
    public void Environment_is_the_default_secrets_provider()
    {
        var builder = WebApplication.CreateBuilder();
        Should.NotThrow(() => builder.AddSecrets());

        builder.Configuration["Secrets:Provider"] = "vault9000";
        Should.Throw<InvalidOperationException>(() => builder.AddSecrets());
    }
}
