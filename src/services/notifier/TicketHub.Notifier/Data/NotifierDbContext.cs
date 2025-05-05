using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TicketHub.BuildingBlocks.Persistence;

namespace TicketHub.Notifier.Data;

public sealed class SentEmail
{
    public Guid Id { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string Template { get; set; }
    public required string Recipient { get; set; }
    public required string Subject { get; set; }
    public DateTimeOffset SentAt { get; set; }
}

public sealed class EventInfo
{
    public Guid EventId { get; set; }
    public required string Title { get; set; }
    public required string VenueName { get; set; }
    public required string City { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public bool Cancelled { get; set; }
}

public sealed class NotifierDbContext(DbContextOptions<NotifierDbContext> options) : DbContext(options)
{
    public DbSet<SentEmail> SentEmails => Set<SentEmail>();
    public DbSet<EventInfo> Events => Set<EventInfo>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddInbox();

        model.Entity<SentEmail>(e =>
        {
            e.ToTable("sent_emails");
            e.Property(x => x.IdempotencyKey).HasMaxLength(200);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.Property(x => x.Template).HasMaxLength(50);
            e.Property(x => x.Recipient).HasMaxLength(256);
            e.Property(x => x.Subject).HasMaxLength(300);
        });

        model.Entity<EventInfo>(e =>
        {
            e.ToTable("event_info");
            e.HasKey(x => x.EventId);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.VenueName).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(100);
        });

        model.ForSqliteTests(Database);
    }
}

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<NotifierDbContext>
{
    public NotifierDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotifierDbContext>()
            .UseSqlServer("Server=localhost;Database=TicketHub_Notifier;TrustServerCertificate=True")
            .Options);
}
