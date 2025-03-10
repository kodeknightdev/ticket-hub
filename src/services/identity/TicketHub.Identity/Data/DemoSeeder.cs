using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketHub.Contracts;

namespace TicketHub.Identity.Data;

public static class DemoSeeder
{
    public static async Task SeedAsync(IdentityDbContext db, IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var hasher = services.GetRequiredService<IPasswordHasher<User>>();
        var password = config["SEED_DEMO_PASSWORD"];
        if (string.IsNullOrWhiteSpace(password)) return;

        (Guid Id, string Email, string Name, string Role)[] demo =
        [
            (DemoAccounts.Buyer, "buyer@tickethub.local", "Bea Buyer", Roles.Buyer),
            (DemoAccounts.SecondBuyer, "buyer2@tickethub.local", "Ben Buyer", Roles.Buyer),
            (DemoAccounts.Organizer, "organizer@tickethub.local", "Riverside Live", Roles.Organizer),
            (DemoAccounts.SecondOrganizer, "organizer2@tickethub.local", "Northside Promotions", Roles.Organizer),
            (DemoAccounts.Staff, "staff@tickethub.local", "Sam Door", Roles.Staff),
            (DemoAccounts.Owner, "owner@tickethub.local", "Olivia Owner", Roles.Owner),
            (DemoAccounts.Admin, "admin@tickethub.local", "Ada Admin", Roles.Admin)
        ];

        var existing = await db.Users.Select(u => u.Id).ToListAsync();
        foreach (var (id, email, name, role) in demo.Where(d => !existing.Contains(d.Id)))
        {
            var user = new User { Id = id, Email = email, DisplayName = name, Role = role, CreatedAt = DateTimeOffset.UtcNow };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
        }

        await db.SaveChangesAsync();
    }
}
