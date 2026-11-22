using Grid.Api.Features.Consumers;
using Grid.Api.Features.ProviderLeads;
using Grid.Api.Features.Providers;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Data;

public sealed partial class GridDbContext
{
    public DbSet<ProviderLead> ProviderLeads => Set<ProviderLead>();

    private static void ConfigureProviderLeads(ModelBuilder model)
    {
        // Human-readable numbers: L-1001.
        model.HasSequence<int>("provider_lead_number_seq").StartsAt(1001);

        model.Entity<ProviderLead>(e =>
        {
            e.Property(l => l.Number).HasDefaultValueSql("nextval('provider_lead_number_seq')");
            e.HasIndex(l => l.Number).IsUnique();
            // One lead per phone; re-applying updates it.
            e.HasIndex(l => l.PhoneNumber).IsUnique();
            e.HasIndex(l => l.CreatedAt);
            e.HasIndex(l => l.ConsumerId);
            e.HasIndex(l => l.ProviderAccountId);
            e.Property(l => l.PhoneNumber).HasMaxLength(12);
            e.Property(l => l.FullName).HasMaxLength(120);
            e.Property(l => l.Area).HasMaxLength(100);
            e.Property(l => l.Experience).HasMaxLength(8);
            e.Property(l => l.Source).HasMaxLength(100);
            e.Property(l => l.Medium).HasMaxLength(100);
            e.Property(l => l.Campaign).HasMaxLength(100);
            e.Property(l => l.SmsProviderMessageId).HasMaxLength(100);
            e.Property(l => l.ContactNote).HasMaxLength(2000);
            e.Ignore(l => l.Reference);
            e.HasOne<Consumer>().WithMany().HasForeignKey(l => l.ConsumerId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<ProviderAccount>().WithMany().HasForeignKey(l => l.ProviderAccountId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Consumer>().WithMany().HasForeignKey(l => l.ContactedByConsumerId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
