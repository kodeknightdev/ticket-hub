using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TicketHub.BuildingBlocks.Persistence;

namespace TicketHub.Identity.Data;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(120);
            e.Property(x => x.Role).HasMaxLength(20);
            e.Property(x => x.PasswordHash).HasMaxLength(400);
        });

        model.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        model.ForSqliteTests(Database);
    }
}

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=localhost;Database=TicketHub_Identity;Trusted_Connection=False;TrustServerCertificate=True")
            .Options);
}
