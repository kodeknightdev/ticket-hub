using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using StackExchange.Redis;
using TicketHub.BuildingBlocks.Hosting;

namespace TicketHub.BffKit;

public sealed record LoginRequest(string Email, string Password);

public sealed record BffUser(Guid Id, string Email, string Name, string Role);

public static class BffSession
{
    public const string UserPolicy = "bff-user";

    public static BffOptions AddBffSession(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(BffOptions.Section).Get<BffOptions>() ?? new BffOptions();
        var services = builder.Services;
        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);

        var dataProtection = services.AddDataProtection().SetApplicationName($"tickethub-{options.AppName}");
        if (options.TokenStore == "memory")
        {
            services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        }
        else
        {
            var redisConfig = ConfigurationOptions.Parse(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
            redisConfig.AbortOnConnectFail = false;
            var redis = ConnectionMultiplexer.Connect(redisConfig);
            services.TryAddSingleton<IConnectionMultiplexer>(redis);
            services.AddSingleton<ITokenStore, RedisTokenStore>();
            // Keys live in Redis so every replica can read the cookie; the key name is per BFF on purpose.
            dataProtection.PersistKeysToStackExchangeRedis(redis, $"bff:{options.AppName}:dp-keys");
            services.AddHealthChecks().AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: "redis", tags: [ServiceDefaults.ReadyTag]);
        }

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = options.CookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = TimeSpan.FromHours(options.SessionHours);
                o.SlidingExpiration = true;
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                o.Events.OnValidatePrincipal = async ctx =>
                {
                    var sid = ctx.Principal?.FindFirst(BffClaims.SessionId)?.Value;
                    var store = ctx.HttpContext.RequestServices.GetRequiredService<ITokenStore>();
                    if (sid is null || await store.GetAsync(sid) is null)
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });

        services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-XSRF-TOKEN";
            o.Cookie.Name = $"__Host-{options.AppName}-af";
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddAuthorizationBuilder().AddPolicy(UserPolicy, p =>
        {
            p.RequireAuthenticatedUser();
            if (options.AllowedRoles.Length > 0) p.RequireRole(options.AllowedRoles);
        });

        services.AddCors(o => o.AddPolicy("frontend", p => p
            .WithOrigins(options.FrontendOrigin)
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()));

        services.AddHttpClient<IdentityClient>(IdentityClient.Name, c => c.BaseAddress = new Uri(options.IdentityUrl))
            .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
        services.AddSingleton<TokenAccessor>();
        services.AddTransient<BffTokenHandler>();

        return options;
    }

    public static string XsrfCookieName(BffOptions options) => $"XSRF-TOKEN-{options.AppName.ToUpperInvariant()}";

    /// <summary>
    /// State-changing calls need the X-CSRF header (which a cross-site form can't send) and a valid
    /// antiforgery token echoed from the readable XSRF cookie.
    /// </summary>
    public static IApplicationBuilder UseBffCsrf(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path;
        var protectedPath = path.StartsWithSegments("/bff") || path.StartsWithSegments("/api");
        if (protectedPath && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method))
        {
            if (ctx.Request.Headers["X-CSRF"] != "1")
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsJsonAsync(new { title = "Missing X-CSRF header" });
                return;
            }

            try
            {
                await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx);
            }
            catch (AntiforgeryValidationException)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { title = "Antiforgery token is missing or invalid" });
                return;
            }
        }
        await next();
    });

    public static IEndpointRouteBuilder MapBffSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var bff = app.MapGroup("/bff").WithTags("Session");

        bff.MapPost("/login", async (LoginRequest req, HttpContext ctx, IdentityClient identity, ITokenStore store, BffOptions options, IAntiforgery antiforgery) =>
        {
            var tokens = await identity.LoginAsync(req.Email, req.Password, ctx.RequestAborted);
            if (tokens is null) return Results.Problem(statusCode: 401, title: "Invalid email or password");
            if (options.AllowedRoles.Length > 0 && !options.AllowedRoles.Contains(tokens.User.Role))
            {
                await identity.RevokeAsync(tokens.RefreshToken, ctx.RequestAborted);
                return Results.Problem(statusCode: 403, title: $"The {tokens.User.Role} role cannot use this app");
            }

            var sid = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await store.SetAsync(sid, new TokenSet(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAt, tokens.User.Id), TimeSpan.FromDays(7));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", tokens.User.Id.ToString()),
                new Claim("email", tokens.User.Email),
                new Claim("name", tokens.User.DisplayName),
                new Claim("role", tokens.User.Role),
                new Claim(BffClaims.SessionId, sid)
            ], CookieAuthenticationDefaults.AuthenticationScheme, "name", "role"));

            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            ctx.User = principal;
            IssueXsrfCookie(ctx, antiforgery, options);
            return Results.Ok(ToUser(principal));
        });

        bff.MapPost("/logout", async (HttpContext ctx, ITokenStore store, IdentityClient identity, BffOptions options, IAntiforgery antiforgery) =>
        {
            var sid = ctx.User.FindFirst(BffClaims.SessionId)?.Value;
            if (sid is not null)
            {
                var tokens = await store.GetAsync(sid);
                if (tokens is not null) await identity.RevokeAsync(tokens.RefreshToken, ctx.RequestAborted);
                await store.RemoveAsync(sid);
            }
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
            IssueXsrfCookie(ctx, antiforgery, options);
            return Results.NoContent();
        });

        bff.MapGet("/user", (HttpContext ctx, IAntiforgery antiforgery, BffOptions options) =>
        {
            IssueXsrfCookie(ctx, antiforgery, options);
            var authenticated = ctx.User.Identity?.IsAuthenticated == true;
            return Results.Ok(new { isAuthenticated = authenticated, user = authenticated ? ToUser(ctx.User) : null });
        });

        return app;
    }

    private static void IssueXsrfCookie(HttpContext ctx, IAntiforgery antiforgery, BffOptions options)
    {
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        ctx.Response.Cookies.Append(XsrfCookieName(options), tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
    }

    public static BffUser ToUser(ClaimsPrincipal user) => new(
        Guid.TryParse(user.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty,
        user.FindFirst("email")?.Value ?? "",
        user.FindFirst("name")?.Value ?? "",
        user.FindFirst("role")?.Value ?? "");
}
