using System.Threading.RateLimiting;
using TicketHub.BffKit;
using TicketHub.BuildingBlocks.Hosting;
using TicketHub.Protos.Seating;
using TicketHub.TicketingBff.Clients;
using TicketHub.TicketingBff.Endpoints;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("ticketing-bff");
var options = builder.AddBffSession();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Hold spam is the cheapest way to lock everyone else out of a hot on-sale, so cap it per user.
    o.AddPolicy("holds", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimits:HoldsPerWindow", 10),
            Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimits:WindowSeconds", 30)),
            QueueLimit = 0
        }));
});

string Service(string name, string fallback) => builder.Configuration[$"Services:{name}"] ?? fallback;

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
builder.Services.AddScoped<ISeatingReader, GrpcSeatingReader>();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(ctx =>
    {
        // The session cookie stays at the edge; services only ever see a bearer token.
        ctx.AddRequestHeaderRemove("Cookie");
        ctx.AddRequestTransform(async t =>
        {
            if (t.HttpContext.User.Identity?.IsAuthenticated != true) return;
            var token = await t.HttpContext.RequestServices.GetRequiredService<TokenAccessor>().GetAccessTokenAsync(t.HttpContext);
            if (token is not null) t.ProxyRequest.Headers.Authorization = new("Bearer", token);
        });
    });

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("frontend");
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseBffCsrf();

app.MapDefaultEndpoints();
app.MapBffSessionEndpoints();
app.MapAggregationEndpoints();
app.MapReverseProxy();

app.Run();

public partial class Program;

/// <summary>Marker for WebApplicationFactory in tests.</summary>
public sealed class TicketingBffApp;
