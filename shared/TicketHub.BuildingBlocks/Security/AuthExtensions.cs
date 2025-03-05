using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using TicketHub.Contracts;

namespace TicketHub.BuildingBlocks.Security;

public static class AuthExtensions
{
    public static IHostApplicationBuilder AddJwtAuth(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(options.SigningKey) || options.SigningKey.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be set and at least 32 characters");

        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    NameClaimType = "name",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("organizer", p => p.RequireRole(Roles.Organizer, Roles.Admin))
            .AddPolicy("staff", p => p.RequireRole(Roles.Staff, Roles.Organizer, Roles.Admin))
            .AddPolicy("admin", p => p.RequireRole(Roles.Admin, Roles.Owner))
            .AddPolicy("analytics", p => p.RequireRole(Roles.Owner, Roles.Organizer, Roles.Admin));

        return builder;
    }

    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    public static string? Email(this ClaimsPrincipal user) => user.FindFirstValue("email");

    public static string? Role(this ClaimsPrincipal user) => user.FindFirstValue("role");

    public static bool IsPrivileged(this ClaimsPrincipal user) => user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Owner);
}
