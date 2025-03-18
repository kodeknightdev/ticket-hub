using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using TicketHub.BuildingBlocks.Hosting;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.BuildingBlocks.Security;
using TicketHub.Contracts;
using TicketHub.Seating.Data;
using TicketHub.Seating.Endpoints;
using TicketHub.Seating.Grpc;
using TicketHub.Seating.Holds;
using TicketHub.Seating.Hubs;
using TicketHub.Seating.Layout;
using TicketHub.Seating.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("seating");
builder.AddJwtAuth();
builder.AddKafka();
builder.AddKafkaConsumer("seating", c => c
    .On<OrderPaidHandler>(Topics.OrderPaid)
    .On<EventLifecycleHandler>(Topics.EventPublished, Topics.EventUpdated, Topics.EventCancelled));
builder.AddKafkaConsumer("seating-broadcast", c => c
    .On<SeatBroadcastHandler>(Topics.SeatHeld, Topics.SeatReleased, Topics.HoldExpired, Topics.SeatsSold, Topics.PriceUpdated));

var sql = builder.Configuration.GetConnectionString("Default");
var redisConfig = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

builder.Services.AddDbContext<SeatingDbContext>(o => o.UseSqlServer(sql, s => s.EnableRetryOnFailure()));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse(redisConfig);
    options.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddHealthChecks()
    .AddSqlServer(sql ?? "", name: "sqlserver", tags: [ServiceDefaults.ReadyTag])
    .AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: "redis", tags: [ServiceDefaults.ReadyTag]);

builder.Services.Configure<SeatingOptions>(builder.Configuration.GetSection(SeatingOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<HoldStore>();
builder.Services.AddScoped<HoldService>();
builder.Services.AddScoped<LayoutCache>();
builder.Services.AddHttpClient<ILayoutSource, EventsLayoutClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Events"] ?? "http://localhost:5202"))
    .AddStandardResilienceHandler();
builder.Services.AddHostedService<HoldExpiryWorker>();

builder.Services.AddGrpc();
builder.Services.AddSignalR().AddStackExchangeRedis(redisConfig, o =>
    o.Configuration.ChannelPrefix = RedisChannel.Literal("tickethub-seating"));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapSeatingEndpoints();
app.MapGrpcService<SeatingRpcService>();
app.MapHub<SeatingHub>("/hubs/seating");

await app.MigrateAndSeedAsync<SeatingDbContext>();

app.Run();

public partial class Program;
