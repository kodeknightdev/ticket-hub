using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TicketHub.BuildingBlocks.Persistence;

namespace TicketHub.Seating.Data;

public sealed class SoldSeat
{
    public Guid EventId { get; set; }
    public Guid SeatId { get; set; }
    public Guid OrderId { get; set; }
    public DateTimeOffset SoldAt { get; set; }
}

public sealed class SeatingEvent
{
    public Guid EventId { get; set; }
    public required string Title { get; set; }
    public int Capacity { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public bool Cancelled { get; set; }
}

public sealed class SeatingDbContext(DbContextOptions<SeatingDbContext> options) : DbContext(options)
{
    public DbSet<SoldSeat> SoldSeats => Set<SoldSeat>();
    public DbSet<SeatingEvent> Events => Set<SeatingEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddInbox();

        // The composite key is the last line of defence against selling a seat twice.
        model.Entity<SoldSeat>(e =>
        {
            e.ToTable("sold_seats");
            e.HasKey(x => new { x.EventId, x.SeatId });
            e.HasIndex(x => x.OrderId);
        });

        model.Entity<SeatingEvent>(e =>
        {
            e.ToTable("seating_events");
            e.HasKey(x => x.EventId);
            e.Property(x => x.Title).HasMaxLength(200);
        });

        model.ForSqliteTests(Database);
    }
}

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SeatingDbContext>
{
    public SeatingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SeatingDbContext>()
            .UseSqlServer("Server=localhost;Database=TicketHub_Seating;TrustServerCertificate=True")
            .Options);
}
