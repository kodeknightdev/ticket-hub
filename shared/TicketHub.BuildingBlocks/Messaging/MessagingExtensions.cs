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
        var kafka = ReadOptions(builder.Configuration);
        builder.Services.Configure<KafkaOptions>(o =>
        {
            builder.Configuration.GetSection(KafkaOptions.Section).Bind(o);
            o.Provider = kafka.Provider;
            o.EventHubsConnectionString = kafka.EventHubsConnectionString;
        });

        if (kafka.Enabled)
        {
            builder.Services.TryAddSingleton<IEventPublisher, KafkaEventPublisher>();
            builder.Services.AddHealthChecks().AddKafka(kafka.Apply(new Confluent.Kafka.ProducerConfig { MessageTimeoutMs = 3000 }),
                topic: "health-checks", name: "kafka", tags: ["ready"]);
        }
        else
        {
            builder.Services.TryAddSingleton<IEventPublisher, NullEventPublisher>();
        }

        return builder;
    }

    private static KafkaOptions ReadOptions(IConfiguration configuration)
    {
        var options = new KafkaOptions();
        configuration.GetSection(KafkaOptions.Section).Bind(options);
        options.Provider = (configuration["Messaging:Provider"] ?? MessagingProviders.Kafka).ToLowerInvariant();
        options.EventHubsConnectionString = configuration["Messaging:EventHubs:ConnectionString"];
        return options;
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
