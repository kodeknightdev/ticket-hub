using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketHub.BuildingBlocks.Hosting;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.BuildingBlocks.Security;
using TicketHub.Contracts;
using TicketHub.Orders.Clients;
using TicketHub.Orders.Data;
using TicketHub.Orders.Endpoints;
using TicketHub.Orders.Messaging;
using TicketHub.Orders.Ordering;
using TicketHub.Orders.Payments;
using TicketHub.Orders.Sagas;
using TicketHub.Orders.Tickets;
using TicketHub.Protos.Seating;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("orders");
builder.AddJwtAuth();
builder.AddKafka();
builder.AddKafkaConsumer("orders", c => c
    .On<SeatsSoldHandler>(Topics.SeatsSold)
    .On<HoldExpiredHandler>(Topics.HoldExpired)
    .On<EventCancelledHandler>(Topics.EventCancelled));

var sql = builder.Configuration.GetConnectionString("Default");
builder.Services.AddDbContext<OrdersDbContext>(o => o.UseSqlServer(sql, s => s.EnableRetryOnFailure()));
builder.Services.AddHealthChecks().AddSqlServer(sql ?? "", name: "sqlserver", tags: [ServiceDefaults.ReadyTag]);
builder.Services.AddHostedService<OutboxDispatcher<OrdersDbContext>>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.Section));
builder.Services.Configure<MockPaymentOptions>(builder.Configuration.GetSection(MockPaymentOptions.Section));
builder.Services.Configure<TicketOptions>(builder.Configuration.GetSection(TicketOptions.Section));
builder.Services.AddSingleton<IPaymentProvider>(sp =>
{
    var stripe = sp.GetRequiredService<IOptions<StripeOptions>>();
    return stripe.Value.IsConfigured
        ? ActivatorUtilities.CreateInstance<StripePaymentProvider>(sp)
        : ActivatorUtilities.CreateInstance<MockPaymentProvider>(sp);
});
builder.Services.AddSingleton<TicketSigner>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<CheckInService>();
builder.Services.AddScoped<CancellationSagaOrchestrator>();
builder.Services.AddHostedService<CancellationSagaWorker>();
builder.Services.AddHostedService<PendingOrderSweeper>();

builder.Services.AddGrpcClient<SeatingRpc.SeatingRpcClient>(o =>
        o.Address = new Uri(builder.Configuration["Services:SeatingGrpc"] ?? "http://localhost:5213"))
    .AddStandardResilienceHandler();
builder.Services.AddScoped<ISeatingClient, GrpcSeatingClient>();
builder.Services.AddHttpClient<IPricingClient, PricingClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Pricing"] ?? "http://localhost:8001"))
    .AddStandardResilienceHandler();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapOrderEndpoints();
app.MapTicketEndpoints();
app.MapWebhookEndpoints();

await app.MigrateAndSeedAsync<OrdersDbContext>();

app.Logger.LogInformation("Payment provider: {Provider}", app.Services.GetRequiredService<IPaymentProvider>().Name);
app.Run();

public partial class Program;
