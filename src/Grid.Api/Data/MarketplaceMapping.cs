using Grid.Api.Features.Catalogue;
using Grid.Api.Features.Consumers;
using Grid.Api.Features.Marketplace;
using Grid.Api.Features.Properties;
using Grid.Api.Features.Providers;
using Grid.Api.Features.Support;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Data;

public sealed partial class GridDbContext
{
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<JobOffer> JobOffers => Set<JobOffer>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobEvent> JobEvents => Set<JobEvent>();
    public DbSet<ProviderEarning> ProviderEarnings => Set<ProviderEarning>();
    public DbSet<ProviderPayout> ProviderPayouts => Set<ProviderPayout>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<ServiceRequestLine> ServiceRequestLines => Set<ServiceRequestLine>();
    public DbSet<Features.ServicePayments.ServicePayment> ServicePayments => Set<Features.ServicePayments.ServicePayment>();
    public DbSet<Review> Reviews => Set<Review>();

    private static void ConfigureMarketplace(ModelBuilder model)
    {
        // Human-readable numbers: GR-1001, GJ-1001, PO-1001, GRID-1001.
        model.HasSequence<int>("service_request_number_seq").StartsAt(1001);
        model.HasSequence<int>("job_number_seq").StartsAt(1001);
        model.HasSequence<int>("provider_payout_number_seq").StartsAt(1001);
        model.HasSequence<int>("support_ticket_number_seq").StartsAt(1001);

        model.Entity<Property>(e =>
        {
            e.HasOne<ServiceArea>().WithMany()
                .HasForeignKey(p => p.ServiceAreaId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<ProviderAccount>(e =>
        {
            e.Property(a => a.TravelRadiusKm).HasPrecision(6, 1);
            // The dispatcher's first filter: who is approved and online right now.
            e.HasIndex(a => new { a.OperatingStatus, a.VerificationStatus });
        });

        model.Entity<ServiceRequest>(e =>
        {
            e.Property(r => r.Number).HasDefaultValueSql("nextval('service_request_number_seq')");
            e.HasIndex(r => r.Number).IsUnique();
            e.HasIndex(r => new { r.CustomerConsumerId, r.CreatedAt });
            // The dispatcher's queue.
            e.HasIndex(r => r.NextDispatchAt).HasFilter("status = 'Searching'");
            e.Property(r => r.Title).HasMaxLength(100);
            e.Property(r => r.Scope).HasMaxLength(200);
            e.Property(r => r.Notes).HasMaxLength(1000);
            e.Property(r => r.Units).HasPrecision(6, 2);
            e.Property(r => r.CustomerName).HasMaxLength(200);
            e.Property(r => r.Area).HasMaxLength(100);
            e.Property(r => r.City).HasMaxLength(100);
            e.Property(r => r.Address).HasMaxLength(200);
            e.Property(r => r.GhanaPostGps).HasMaxLength(16);
            e.Property(r => r.CancelReason).HasMaxLength(300);
            e.Property(r => r.Total).HasPrecision(12, 2);
            e.Property(r => r.IdempotencyKeyHash).HasMaxLength(64);
            e.HasIndex(r => new { r.CustomerConsumerId, r.IdempotencyKeyHash })
                .IsUnique()
                .HasFilter("idempotency_key_hash IS NOT NULL")
                .HasDatabaseName("ix_service_requests_one_per_key");
            e.HasOne<ProviderAccount>().WithMany().HasForeignKey(r => r.PreferredProviderAccountId).OnDelete(DeleteBehavior.SetNull);
            e.Ignore(r => r.Reference);
            e.HasOne<Consumer>().WithMany().HasForeignKey(r => r.CustomerConsumerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Property>().WithMany().HasForeignKey(r => r.PropertyId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<ServiceCategory>().WithMany().HasForeignKey(r => r.ServiceCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ServiceCategory>().WithMany().HasForeignKey(r => r.SubServiceCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ServiceArea>().WithMany().HasForeignKey(r => r.ServiceAreaId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ServiceRequestLine>(e =>
        {
            e.HasIndex(l => new { l.ServiceRequestId, l.SortOrder });
            e.Property(l => l.Name).HasMaxLength(170);
            e.Property(l => l.UnitLabel).HasMaxLength(20);
            e.Property(l => l.UnitPrice).HasPrecision(12, 2);
            e.Property(l => l.Total).HasPrecision(12, 2);
            e.HasOne<ServiceRequest>().WithMany().HasForeignKey(l => l.ServiceRequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ServicePackage>().WithMany().HasForeignKey(l => l.ServicePackageId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Features.ServicePayments.ServicePayment>(e =>
        {
            e.HasIndex(p => p.ClientReference).IsUnique();
            e.HasIndex(p => p.ServiceRequestId);
            e.HasIndex(p => p.NextCheckAt).HasFilter("next_check_at IS NOT NULL");
            // At most one payment per booking can be collected.
            e.HasIndex(p => p.ServiceRequestId)
                .IsUnique()
                .HasFilter("state = 'Succeeded'")
                .HasDatabaseName("ix_service_payments_one_paid_per_request");
            e.Property(p => p.Amount).HasPrecision(12, 2);
            e.Property(p => p.AmountPaid).HasPrecision(12, 2);
            e.Property(p => p.Charges).HasPrecision(12, 2);
            e.Property(p => p.Channel).HasMaxLength(40);
            e.Property(p => p.PayerPhone).HasMaxLength(12);
            e.Property(p => p.ClientReference).HasMaxLength(40);
            e.Property(p => p.Error).HasMaxLength(100);
            e.Property(p => p.HubtelTransactionId).HasMaxLength(100);
            e.Property(p => p.HubtelOrderId).HasMaxLength(100);
            e.Property(p => p.RefundReason).HasMaxLength(60);
            e.Ignore(p => p.IsPaid);
            e.HasOne<ServiceRequest>().WithMany().HasForeignKey(p => p.ServiceRequestId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Consumer>().WithMany().HasForeignKey(p => p.ConsumerId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Review>(e =>
        {
            e.HasIndex(r => r.JobId).IsUnique();
            e.HasIndex(r => r.ProviderAccountId);
            e.Property(r => r.Comment).HasMaxLength(1000);
            e.HasOne<Job>().WithMany().HasForeignKey(r => r.JobId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProviderAccount>().WithMany().HasForeignKey(r => r.ProviderAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Consumer>().WithMany().HasForeignKey(r => r.CustomerConsumerId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<JobOffer>(e =>
        {
            // A provider is offered a request once.
            e.HasIndex(o => new { o.ServiceRequestId, o.ProviderAccountId }).IsUnique();
            e.HasIndex(o => new { o.ProviderAccountId, o.Status, o.ExpiresAt });
            e.Property(o => o.Price).HasPrecision(12, 2);
            e.Property(o => o.DistanceKm).HasPrecision(6, 1);
            e.Property(o => o.DeclineReason).HasMaxLength(200);
            e.HasOne<ServiceRequest>().WithMany().HasForeignKey(o => o.ServiceRequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ProviderAccount>().WithMany().HasForeignKey(o => o.ProviderAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Job>(e =>
        {
            e.Property(j => j.Number).HasDefaultValueSql("nextval('job_number_seq')");
            e.HasIndex(j => j.Number).IsUnique();
            e.HasIndex(j => j.JobOfferId).IsUnique();
            e.HasIndex(j => new { j.ProviderAccountId, j.ScheduledAt });
            // A request has one job at a time; a cancelled one makes room for the next.
            e.HasIndex(j => j.ServiceRequestId)
                .IsUnique()
                .HasFilter("stage <> 'Cancelled'")
                .HasDatabaseName("ix_jobs_one_live_per_request");
            e.HasIndex(j => j.AutoConfirmAt).HasFilter("stage = 'AwaitingConfirmation'");
            e.Property(j => j.Price).HasPrecision(12, 2);
            e.Property(j => j.FeePercent).HasPrecision(5, 2);
            e.Property(j => j.Fee).HasPrecision(12, 2);
            e.Property(j => j.Net).HasPrecision(12, 2);
            e.Property(j => j.DistanceKm).HasPrecision(6, 1);
            e.Property(j => j.CancelReason).HasMaxLength(300);
            e.Ignore(j => j.Code);
            e.Ignore(j => j.IsActive);
            e.HasOne<ServiceRequest>().WithMany().HasForeignKey(j => j.ServiceRequestId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<JobOffer>().WithMany().HasForeignKey(j => j.JobOfferId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProviderAccount>().WithMany().HasForeignKey(j => j.ProviderAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Consumer>().WithMany().HasForeignKey(j => j.CustomerConsumerId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<JobEvent>(e =>
        {
            e.HasIndex(x => new { x.JobId, x.CreatedAt });
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ProviderEarning>(e =>
        {
            e.HasIndex(x => x.JobId).IsUnique();
            e.HasIndex(x => new { x.ProviderAccountId, x.Status });
            e.Property(x => x.Gross).HasPrecision(12, 2);
            e.Property(x => x.Fee).HasPrecision(12, 2);
            e.Property(x => x.Net).HasPrecision(12, 2);
            e.HasOne<ProviderAccount>().WithMany().HasForeignKey(x => x.ProviderAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProviderPayout>().WithMany().HasForeignKey(x => x.PayoutId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ProviderPayout>(e =>
        {
            e.Property(p => p.Number).HasDefaultValueSql("nextval('provider_payout_number_seq')");
            e.HasIndex(p => p.Number).IsUnique();
            e.HasIndex(p => new { p.ProviderAccountId, p.CreatedAt });
            e.Property(p => p.Amount).HasPrecision(12, 2);
            e.Property(p => p.Destination).HasMaxLength(160);
            e.Property(p => p.ExternalReference).HasMaxLength(100);
            e.Ignore(p => p.Reference);
            e.HasOne<ProviderAccount>().WithMany().HasForeignKey(p => p.ProviderAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Consumer>().WithMany().HasForeignKey(p => p.RecordedByConsumerId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<SupportTicket>(e =>
        {
            e.Property(t => t.Number).HasDefaultValueSql("nextval('support_ticket_number_seq')");
            e.HasIndex(t => t.Number).IsUnique();
            e.HasIndex(t => new { t.ConsumerId, t.CreatedAt });
            e.HasIndex(t => new { t.Status, t.CreatedAt });
            e.Property(t => t.Subject).HasMaxLength(120);
            e.Property(t => t.Details).HasMaxLength(4000);
            e.Property(t => t.ResolutionNote).HasMaxLength(2000);
            e.Ignore(t => t.Reference);
            e.HasOne<Consumer>().WithMany().HasForeignKey(t => t.ConsumerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProviderAccount>().WithMany().HasForeignKey(t => t.ProviderAccountId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Job>().WithMany().HasForeignKey(t => t.JobId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
