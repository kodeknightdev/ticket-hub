using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TicketHub.BuildingBlocks.Messaging;

public static class MessagingExtensions
{
    public static IHostApplicationBuilder AddKafka(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.Section));
        var enabled = builder.Configuration.GetValue(KafkaOptions.Section + ":Enabled", true);

        if (enabled)
        {
            builder.Services.TryAddSingleton<IEventPublisher, KafkaEventPublisher>();
            var servers = builder.Configuration[KafkaOptions.Section + ":BootstrapServers"] ?? "localhost:9092";
            builder.Services.AddHealthChecks().AddKafka(new Confluent.Kafka.ProducerConfig
            {
                BootstrapServers = servers,
                MessageTimeoutMs = 3000
            }, topic: "health-checks", name: "kafka", tags: ["ready"]);
        }
        else
        {
            builder.Services.TryAddSingleton<IEventPublisher, NullEventPublisher>();
        }

        return builder;
    }

    public static IHostApplicationBuilder AddKafkaConsumer(this IHostApplicationBuilder builder, string groupId, Action<ConsumerRegistration> configure)
    {
        var registration = new ConsumerRegistration { GroupId = groupId };
        configure(registration);
        foreach (var handler in registration.Handlers.Values.Distinct())
            builder.Services.TryAddScoped(handler);

        if (!builder.Configuration.GetValue(KafkaOptions.Section + ":Enabled", true)) return builder;

        builder.Services.AddSingleton<IHostedService>(sp => new KafkaConsumerService(
            registration,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<IOptions<KafkaOptions>>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<KafkaConsumerService>>()));
        return builder;
    }
}
