using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Hosting;
using TicketHub.BuildingBlocks.Security;
using TicketHub.Identity.Auth;
using TicketHub.Identity.Data;
using TicketHub.Identity.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("identity");
builder.AddJwtAuth();

var connection = builder.Configuration.GetConnectionString("Default");
builder.Services.AddDbContext<IdentityDbContext>(o => o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure()));
builder.Services.AddHealthChecks().AddSqlServer(connection ?? "", name: "sqlserver", tags: [ServiceDefaults.ReadyTag]);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterValidator>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();

await app.MigrateAndSeedAsync<IdentityDbContext>(DemoSeeder.SeedAsync);

app.Run();

public partial class Program;
