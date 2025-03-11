using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TicketHub.BuildingBlocks.Persistence;

namespace TicketHub.Events.Data;

public sealed class EventsDbContext(DbContextOptions<EventsDbContext> options) : DbContext(options)
{
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<PriceTier> Tiers => Set<PriceTier>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddOutbox().AddInbox();

        model.Entity<Venue>(e =>
        {
            e.ToTable("venues");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(100);
        });

        model.Entity<Event>(e =>
        {
            e.ToTable("events");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Artist).HasMaxLength(200);
            e.Property(x => x.Category).HasMaxLength(50);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.VenueName).HasMaxLength(200);
            e.Property(x => x.CoverImageUrl).HasMaxLength(500);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.Status, x.StartsAt });
            e.HasIndex(x => x.OrganizerId);
            e.HasMany(x => x.Sections).WithOne().HasForeignKey(s => s.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Tiers).WithOne().HasForeignKey(t => t.EventId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.Capacity);
        });

        model.Entity<Section>(e =>
        {
            e.ToTable("sections");
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasMany(x => x.Seats).WithOne().HasForeignKey(s => s.SectionId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Seat>(e =>
        {
            e.ToTable("seats");
            e.Property(x => x.Row).HasMaxLength(5);
            e.HasIndex(x => x.EventId);
            e.HasIndex(x => new { x.SectionId, x.Row, x.Number }).IsUnique();
        });

        model.Entity<PriceTier>(e =>
        {
            e.ToTable("price_tiers");
            e.Property(x => x.Name).HasMaxLength(50);
            e.Property(x => x.BasePrice).HasPrecision(10, 2);
            e.Property(x => x.MinPrice).HasPrecision(10, 2);
            e.Property(x => x.MaxPrice).HasPrecision(10, 2);
            e.Property(x => x.CurrentPrice).HasPrecision(10, 2);
        });

        model.ForSqliteTests(Database);
    }
}

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<EventsDbContext>
{
    public EventsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<EventsDbContext>()
            .UseSqlServer("Server=localhost;Database=TicketHub_Events;TrustServerCertificate=True")
            .Options);
}
