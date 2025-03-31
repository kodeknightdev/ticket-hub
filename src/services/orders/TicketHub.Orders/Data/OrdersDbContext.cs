using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TicketHub.BuildingBlocks.Persistence;

namespace TicketHub.Orders.Data;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<PaymentWebhookEvent> WebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<CancellationSaga> CancellationSagas => Set<CancellationSaga>();
    public DbSet<CancellationSagaStep> CancellationSagaSteps => Set<CancellationSagaStep>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddOutbox().AddInbox();

        model.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.HasIndex(x => new { x.BuyerId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => x.HoldId);
            e.HasIndex(x => new { x.EventId, x.Status });
            e.HasIndex(x => x.PaymentRef);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.BuyerEmail).HasMaxLength(256);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.PaymentProvider).HasMaxLength(20);
            e.Property(x => x.PaymentRef).HasMaxLength(100);
            e.Property(x => x.ClientSecret).HasMaxLength(200);
            e.Property(x => x.StatusReason).HasMaxLength(200);
            e.Property(x => x.Subtotal).HasPrecision(10, 2);
            e.Property(x => x.Fees).HasPrecision(10, 2);
            e.Property(x => x.Total).HasPrecision(10, 2);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Tickets).WithOne().HasForeignKey(t => t.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<OrderLine>(e =>
        {
            e.ToTable("order_lines");
            e.Property(x => x.Label).HasMaxLength(100);
            e.Property(x => x.Price).HasPrecision(10, 2);
        });

        model.Entity<Ticket>(e =>
        {
            e.ToTable("tickets");
            e.Property(x => x.Label).HasMaxLength(100);
            e.Property(x => x.Code).HasMaxLength(120);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.BuyerId);
            e.HasIndex(x => new { x.EventId, x.SeatId });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        model.Entity<PaymentWebhookEvent>(e =>
        {
            e.ToTable("payment_webhook_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(100);
            e.Property(x => x.Provider).HasMaxLength(20);
            e.Property(x => x.Type).HasMaxLength(100);
        });

        model.Entity<CancellationSaga>(e =>
        {
            e.ToTable("cancellation_sagas");
            e.HasIndex(x => x.EventId).IsUnique();
            e.Property(x => x.EventTitle).HasMaxLength(200);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.SagaId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<CancellationSagaStep>(e =>
        {
            e.ToTable("cancellation_saga_steps");
            e.HasIndex(x => new { x.SagaId, x.OrderId }).IsUnique();
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LastError).HasMaxLength(1000);
        });

        model.ForSqliteTests(Database);
    }
}

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlServer("Server=localhost;Database=TicketHub_Orders;TrustServerCertificate=True")
            .Options);
}
