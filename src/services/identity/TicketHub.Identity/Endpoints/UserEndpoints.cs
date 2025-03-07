using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Security;
using TicketHub.Identity.Data;

namespace TicketHub.Identity.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/users").WithTags("Users").RequireAuthorization();

        users.MapGet("/me", async (HttpContext ctx, IdentityDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ctx.User.UserId());
            return user is null ? Results.NotFound() : Results.Ok(AuthEndpoints.ToDto(user));
        });

        users.MapGet("/", async (string? role, IdentityDbContext db) =>
        {
            var query = db.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(role)) query = query.Where(u => u.Role == role);
            var list = await query.OrderBy(u => u.Email).Take(500).ToListAsync();
            return Results.Ok(list.Select(AuthEndpoints.ToDto));
        }).RequireAuthorization("admin");

        users.MapGet("/{id:guid}", async (Guid id, IdentityDbContext db) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            return user is null ? Results.NotFound() : Results.Ok(AuthEndpoints.ToDto(user));
        }).RequireAuthorization("admin");

        return app;
    }
}
