using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Security;
using TicketHub.Contracts;
using TicketHub.Identity.Auth;
using TicketHub.Identity.Data;

namespace TicketHub.Identity.Endpoints;

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
    }
}

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/login", async (LoginRequest req, IdentityDbContext db, IPasswordHasher<User> hasher, TokenService tokens, TimeProvider clock) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
                return Results.Problem(statusCode: 401, title: "Invalid email or password");

            user.LastLoginAt = clock.GetUtcNow();
            var response = await Issue(db, tokens, user);
            return Results.Ok(response);
        });

        auth.MapPost("/refresh", async (RefreshRequest req, IdentityDbContext db, TokenService tokens, TimeProvider clock) =>
        {
            var hash = TokenService.Hash(req.RefreshToken);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
            var now = clock.GetUtcNow();

            if (stored is null) return Results.Problem(statusCode: 401, title: "Unknown refresh token");
            if (stored.RevokedAt is not null && stored.ReplacedBy is not null)
            {
                // A rotated token being replayed means it leaked; kill the whole family.
                await db.RefreshTokens.Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
                return Results.Problem(statusCode: 401, title: "Refresh token reuse detected");
            }
            if (!stored.IsActive(now)) return Results.Problem(statusCode: 401, title: "Refresh token expired");

            var user = await db.Users.FirstAsync(u => u.Id == stored.UserId);
            var response = await Issue(db, tokens, user, stored);
            return Results.Ok(response);
        });

        auth.MapPost("/revoke", async (RefreshRequest req, IdentityDbContext db, TimeProvider clock) =>
        {
            var hash = TokenService.Hash(req.RefreshToken);
            await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()));
            return Results.NoContent();
        });

        auth.MapPost("/register", async (RegisterRequest req, IValidator<RegisterRequest> validator, IdentityDbContext db,
            IPasswordHasher<User> hasher, TokenService tokens, TimeProvider clock) =>
        {
            var validation = await validator.ValidateAsync(req);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var email = req.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Problem(statusCode: 409, title: "Email already registered");

            var user = new User { Id = Guid.NewGuid(), Email = email, DisplayName = req.DisplayName.Trim(), Role = Roles.Buyer, CreatedAt = clock.GetUtcNow() };
            user.PasswordHash = hasher.HashPassword(user, req.Password);
            db.Users.Add(user);

            var response = await Issue(db, tokens, user);
            return Results.Created($"/users/{user.Id}", response);
        });

        return app;
    }

    private static async Task<TokenResponse> Issue(IdentityDbContext db, TokenService tokens, User user, RefreshToken? rotated = null)
    {
        var (access, expires) = tokens.CreateAccessToken(user);
        var (raw, refresh) = tokens.CreateRefreshToken(user.Id);
        db.RefreshTokens.Add(refresh);
        if (rotated is not null)
        {
            rotated.RevokedAt = refresh.CreatedAt;
            rotated.ReplacedBy = refresh.Id;
        }
        await db.SaveChangesAsync();
        return new TokenResponse(access, raw, expires, ToDto(user));
    }

    public static UserDto ToDto(User u) => new(u.Id, u.Email, u.DisplayName, u.Role);
}
