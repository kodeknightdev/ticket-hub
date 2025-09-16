using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using TicketHub.BuildingBlocks.Messaging;

namespace TicketHub.BuildingBlocks.Hosting;

public static class SecretsProviders
{
    public const string Environment = "environment";
    public const string KeyVault = "keyvault";
}

public static class ServiceDefaults
{
    public const string LiveTag = "live";
    public const string ReadyTag = "ready";

    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.AddSecrets();
        builder.Host.UseSerilog((ctx, cfg) => cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", serviceName)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj} {TraceId}{NewLine}{Exception}"));

        builder.AddTelemetry(serviceName);

        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString());
        builder.Services.AddOpenApi();
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        return builder;
    }

    /// <summary>
    /// Secrets:Provider = "environment" (default) reads secrets from env vars like everything else;
    /// "keyvault" also reads the files the Key Vault CSI driver mounts (one file per secret, "__" for ":").
    /// </summary>
    public static IHostApplicationBuilder AddSecrets(this IHostApplicationBuilder builder)
    {
        var provider = (builder.Configuration["Secrets:Provider"] ?? SecretsProviders.Environment).ToLowerInvariant();
        if (provider == SecretsProviders.KeyVault)
        {
            var path = builder.Configuration["Secrets:Path"] ?? "/mnt/secrets-store";
            builder.Configuration.AddKeyPerFile(path, optional: false, reloadOnChange: true);
        }
        else if (provider != SecretsProviders.Environment)
        {
            throw new InvalidOperationException($"Unknown Secrets:Provider '{provider}'");
        }
        return builder;
    }

    public static IHostApplicationBuilder AddTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
            if (!string.IsNullOrWhiteSpace(otlp)) o.AddOtlpExporter();
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/health") && !ctx.Request.Path.StartsWithSegments("/alive"))
                    .AddHttpClientInstrumentation()
                    .AddSource(KafkaTracing.SourceName)
                    .AddSource("Microsoft.EntityFrameworkCore")
                    .AddSource("Yarp.ReverseProxy");
                if (!string.IsNullOrWhiteSpace(otlp)) t.AddOtlpExporter();
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                if (!string.IsNullOrWhiteSpace(otlp)) m.AddOtlpExporter();
            });

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteHealth });
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(LiveTag),
            ResponseWriter = WriteHealth
        });

        app.MapOpenApi();
        app.MapScalarApiReference();

        return app;
    }

    private static Task WriteHealth(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                error = e.Value.Exception?.Message
            })
        });
    }
}
