using System.Threading.RateLimiting;
using TicketHub.BffKit;
using TicketHub.BuildingBlocks.Hosting;
using TicketHub.DashboardBff.Clients;
using TicketHub.DashboardBff.Endpoints;
using TicketHub.DashboardBff.Scoping;
using TicketHub.Protos.Seating;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("dashboard-bff");
var options = builder.AddBffSession();

builder.Services.AddHybridCache(o =>
{
    o.DefaultEntryOptions = new() { Expiration = TimeSpan.FromSeconds(60), LocalCacheExpiration = TimeSpan.FromSeconds(60) };
    o.MaximumPayloadBytes = 4 * 1024 * 1024;
});
if (options.TokenStore != "memory")
    builder.Services.AddStackExchangeRedisCache(o =>
    {
        o.Configuration = builder.Configuration.GetConnectionString("Redis");
        o.InstanceName = "dash-cache:";
    });

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // CSV exports hit every fact table; a handful per minute is plenty for a human.
    o.AddPolicy("exports", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirst("sub")?.Value ?? "anon",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimits:ExportsPerMinute", 5),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

string Service(string name, string fallback) => builder.Configuration[$"Services:{name}"] ?? fallback;

builder.Services.AddHttpClient<AnalyticsApi>(AnalyticsApi.Name, c => c.BaseAddress = new Uri(Service("Analytics", "http://localhost:8002")))
    .AddHttpMessageHandler<BffTokenHandler>()
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<EventsApi>(EventsApi.Name, c => c.BaseAddress = new Uri(Service("Events", "http://localhost:5202")))
    .AddHttpMessageHandler<BffTokenHandler>()
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<PricingApi>(PricingApi.Name, c => c.BaseAddress = new Uri(Service("Pricing", "http://localhost:8001")))
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<OrdersApi>(OrdersApi.Name, c => c.BaseAddress = new Uri(Service("Orders", "http://localhost:5204")))
    .AddHttpMessageHandler<BffTokenHandler>()
    .AddStandardResilienceHandler();
builder.Services.AddGrpcClient<SeatingRpc.SeatingRpcClient>(o => o.Address = new Uri(Service("SeatingGrpc", "http://localhost:5213")))
    .AddStandardResilienceHandler();
builder.Services.AddScoped<ISeatingCounts, GrpcSeatingCounts>();
builder.Services.AddScoped<ScopeResolver>();
builder.Services.AddSingleton<ResponseCache>();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(ctx => ctx.AddRequestHeaderRemove("Cookie"));

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseBffCsrf();

app.MapDefaultEndpoints();
app.MapBffSessionEndpoints();
app.MapDashboardEndpoints();
app.MapExportEndpoints();
app.MapReverseProxy();

app.Run();

public partial class Program;

/// <summary>Marker for WebApplicationFactory in tests.</summary>
public sealed class DashboardBffApp;
