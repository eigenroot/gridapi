using Grid.Api.Features.BillPayments;
using Grid.Api.Features.Consumers;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Data;

public sealed partial class GridDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<FloatTransfer> FloatTransfers => Set<FloatTransfer>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();

    private static void ConfigureBillPayments(ModelBuilder model)
    {
        model.Entity<Order>(e =>
        {
            e.HasOne<Consumer>().WithMany().HasForeignKey(o => o.ConsumerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(o => o.ClientReference).IsUnique();
            e.HasIndex(o => new { o.ConsumerId, o.IdempotencyKeyHash }).IsUnique();
            e.HasIndex(o => new { o.ConsumerId, o.CreatedAt, o.Id });
            e.HasIndex(o => o.NextCheckAt).HasFilter("next_check_at IS NOT NULL");
            e.Property(o => o.ProductCode).HasMaxLength(32);
            e.Property(o => o.AccountNumber).HasMaxLength(20);
            e.Property(o => o.Destination).HasMaxLength(20);
            e.Property(o => o.Bundle).HasMaxLength(100);
            e.Property(o => o.SessionId).HasMaxLength(100);
            e.Property(o => o.ReceiptEmail).HasMaxLength(254);
            e.Property(o => o.CustomerName).HasMaxLength(200);
            e.Property(o => o.ClientReference).HasMaxLength(36);
            e.Property(o => o.Channel).HasMaxLength(20);
            e.Property(o => o.PayerPhone).HasMaxLength(12);
            e.Property(o => o.IdempotencyKeyHash).HasMaxLength(64);
            e.Property(o => o.RequestHash).HasMaxLength(64);
            e.ToTable("orders", t => t.HasCheckConstraint("ck_orders_amounts", "principal > 0 AND fee >= 0 AND total = principal + fee"));
        });

        model.Entity<Bill>(e =>
        {
            e.HasOne<Order>().WithMany().HasForeignKey(b => b.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(b => b.OrderId).IsUnique();
            e.HasIndex(b => b.ClientReference).IsUnique();
            e.HasIndex(b => b.NextCheckAt).HasFilter("next_check_at IS NOT NULL");
            e.Property(b => b.ClientReference).HasMaxLength(36);
            e.Property(b => b.Commission).HasPrecision(18, 4); // Hubtel reports e.g. 0.0198.
            e.ToTable("bills", t => t.HasCheckConstraint("ck_bills_amount", "amount > 0"));
        });

        model.Entity<Refund>(e =>
        {
            e.HasOne<Order>().WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(r => r.OrderId).IsUnique();
            e.HasIndex(r => r.ExternalReference).IsUnique().HasFilter("external_reference IS NOT NULL");
            e.HasIndex(r => r.NextCheckAt).HasFilter("next_check_at IS NOT NULL");
            e.Property(r => r.Reason).HasMaxLength(64);
            e.Property(r => r.Evidence).HasMaxLength(1000);
            e.ToTable("refunds", t => t.HasCheckConstraint("ck_refunds_amount", "amount > 0"));
        });

        model.Entity<FloatTransfer>(e =>
        {
            e.HasIndex(t => t.ClientReference).IsUnique();
            e.HasIndex(t => t.NextCheckAt).HasFilter("next_check_at IS NOT NULL");
            e.Property(t => t.ClientReference).HasMaxLength(36);
            e.ToTable("float_transfers", t => t.HasCheckConstraint("ck_float_transfers_amount", "amount > 0"));
        });

        model.Entity<OrderEvent>(e =>
        {
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.OrderId, x.CreatedAt });
            e.Property(x => x.Kind).HasMaxLength(64);
            e.Property(x => x.Detail).HasMaxLength(500);
        });

        foreach (var type in new[] { typeof(Order), typeof(Bill), typeof(Refund), typeof(FloatTransfer) })
        {
            foreach (var property in model.Entity(type).Metadata.GetProperties())
            {
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                {
                    if (property.GetScale() is null) { property.SetPrecision(18); property.SetScale(2); }
                }
                else if (property.ClrType == typeof(string) && property.GetMaxLength() is null) property.SetMaxLength(100);
            }
        }
    }

    /// <summary>Order history is append-only; a database trigger enforces the same for bulk updates.</summary>
    private void ProtectOrderEvents()
    {
        if (ChangeTracker.Entries<OrderEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Order events are append-only.");
    }
}
