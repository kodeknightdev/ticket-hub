using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Hosting;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.Contracts;
using TicketHub.Notifier.Data;
using TicketHub.Notifier.Email;
using TicketHub.Notifier.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("notifier");
builder.AddKafka();
builder.AddKafkaConsumer("notifier", c => c
    .On<TicketsIssuedHandler>(Topics.TicketsIssued)
    .On<HoldExpiringHandler>(Topics.HoldExpiring)
    .On<OrderRefundedHandler>(Topics.OrderRefunded)
    .On<EventInfoHandler>(Topics.EventPublished, Topics.EventUpdated, Topics.EventCancelled));

var sql = builder.Configuration.GetConnectionString("Default");
builder.Services.AddDbContext<NotifierDbContext>(o => o.UseSqlServer(sql, s => s.EnableRetryOnFailure()));
builder.Services.AddHealthChecks().AddSqlServer(sql ?? "", name: "sqlserver", tags: [ServiceDefaults.ReadyTag]);

builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.Section));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<TemplateRenderer>();
builder.Services.AddScoped<Mailer>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();

app.MapGet("/emails/recent", async (NotifierDbContext db) =>
    Results.Ok(await db.SentEmails.AsNoTracking().OrderByDescending(e => e.SentAt).Take(50).ToListAsync())).WithTags("Emails");

await app.MigrateAndSeedAsync<NotifierDbContext>();

app.Run();

public partial class Program;
