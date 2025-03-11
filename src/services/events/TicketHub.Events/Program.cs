using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Hosting;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.BuildingBlocks.Security;
using TicketHub.Contracts;
using TicketHub.Events.Data;
using TicketHub.Events.Endpoints;
using TicketHub.Events.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("events");
builder.AddJwtAuth();
builder.AddKafka();
builder.AddKafkaConsumer("events", c => c.On<PriceUpdatedHandler>(Topics.PriceUpdated));

var connection = builder.Configuration.GetConnectionString("Default");
builder.Services.AddDbContext<EventsDbContext>(o => o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure()));
builder.Services.AddHealthChecks().AddSqlServer(connection ?? "", name: "sqlserver", tags: [ServiceDefaults.ReadyTag]);
builder.Services.AddHostedService<OutboxDispatcher<EventsDbContext>>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<CreateEventValidator>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapEventEndpoints();

await app.MigrateAndSeedAsync<EventsDbContext>(DemoSeeder.SeedAsync);

app.Run();

public partial class Program;
