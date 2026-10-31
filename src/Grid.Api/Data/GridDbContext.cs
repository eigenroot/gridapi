using Grid.Api.Features.Auth;
using Grid.Api.Features.Catalogue;
using Grid.Api.Features.Consumers;
using Grid.Api.Features.Media;
using Grid.Api.Features.Properties;
using Grid.Api.Features.Providers;
using Grid.Api.Features.UtilityAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Grid.Api.Data;

public sealed class GridDbContext(DbContextOptions<GridDbContext> options, TimeProvider time)
    : DbContext(options)
{
    public DbSet<Consumer> Consumers => Set<Consumer>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<UtilityAccount> UtilityAccounts => Set<UtilityAccount>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<ServiceArea> ServiceAreas => Set<ServiceArea>();
    public DbSet<MediaObject> MediaObjects => Set<MediaObject>();
    public DbSet<ProviderAccount> ProviderAccounts => Set<ProviderAccount>();
    public DbSet<ProviderApplication> ProviderApplications => Set<ProviderApplication>();
    public DbSet<ProviderApplicationSubmission> ProviderApplicationSubmissions => Set<ProviderApplicationSubmission>();
    public DbSet<ProviderOffering> ProviderOfferings => Set<ProviderOffering>();
    public DbSet<ProviderServiceArea> ProviderServiceAreas => Set<ProviderServiceArea>();
    public DbSet<ProviderAvailabilityRule> ProviderAvailabilityRules => Set<ProviderAvailabilityRule>();
    public DbSet<VerificationDocument> VerificationDocuments => Set<VerificationDocument>();
    public DbSet<PortfolioItem> PortfolioItems => Set<PortfolioItem>();
    public DbSet<StandardsAcceptance> StandardsAcceptances => Set<StandardsAcceptance>();
    public DbSet<PayoutMethod> PayoutMethods => Set<PayoutMethod>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Enums are stored as their names so the database reads the same as the API.
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Consumer>(e =>
        {
            e.HasIndex(c => c.PhoneNumber).IsUnique();
            e.Property(c => c.PhoneNumber).HasMaxLength(12);
            e.Property(c => c.FirstName).HasMaxLength(100);
            e.Property(c => c.LastName).HasMaxLength(100);
            e.Property(c => c.Email).HasMaxLength(254);
            e.Property(c => c.KycName).HasMaxLength(200);
            e.Property(c => c.KycProvider).HasMaxLength(32);
            e.Property(c => c.KycLastError).HasMaxLength(500);
            e.Ignore(c => c.FullName);
            e.Ignore(c => c.ProfileComplete);

            // The KYC worker's queue: only rows still waiting on a lookup are indexed.
            e.HasIndex(c => new { c.KycStatus, c.KycNextAttemptAt })
                .HasFilter("kyc_status IN ('NotChecked', 'Pending')");

            e.HasOne<Property>().WithMany()
                .HasForeignKey(c => c.DefaultPropertyId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<OtpChallenge>(e =>
        {
            e.HasIndex(o => new { o.PhoneNumber, o.CreatedAt });
            e.Property(o => o.PhoneNumber).HasMaxLength(12);
            e.Property(o => o.CodeHash).HasMaxLength(64);
            e.Property(o => o.RequestIp).HasMaxLength(64);
            e.Property(o => o.SmsProvider).HasMaxLength(32);
            e.Property(o => o.SmsProviderMessageId).HasMaxLength(100);
        });

        model.Entity<RefreshToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.FamilyId);
            e.Property(t => t.TokenHash).HasMaxLength(64);
            e.Property(t => t.UserAgent).HasMaxLength(300);
            e.Property(t => t.CreatedIp).HasMaxLength(64);
            e.HasOne<Consumer>().WithMany()
                .HasForeignKey(t => t.ConsumerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Property>(e =>
        {
            e.HasIndex(p => p.OwnerConsumerId);
            e.Property(p => p.Nickname).HasMaxLength(60);
            e.Property(p => p.Area).HasMaxLength(100);
            e.Property(p => p.City).HasMaxLength(100);
            e.Property(p => p.Address).HasMaxLength(200);
            e.Property(p => p.GhanaPostGps).HasMaxLength(16);
            e.HasOne<Consumer>().WithMany()
                .HasForeignKey(p => p.OwnerConsumerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<UtilityAccount>(e =>
        {
            e.Property(a => a.AccountNumber).HasMaxLength(20);
            e.Property(a => a.Nickname).HasMaxLength(60);

            // A person can save a given meter/number once per utility, and has one default per utility.
            e.HasIndex(a => new { a.OwnerConsumerId, a.Type, a.AccountNumber })
                .IsUnique()
                .HasFilter("archived_at IS NULL");
            e.HasIndex(a => new { a.OwnerConsumerId, a.Type })
                .IsUnique()
                .HasFilter("is_default AND archived_at IS NULL")
                .HasDatabaseName("ix_utility_accounts_one_default_per_type");

            e.HasOne<Consumer>().WithMany()
                .HasForeignKey(a => a.OwnerConsumerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Property>().WithMany()
                .HasForeignKey(a => a.PropertyId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        ConfigureCatalogue(model);
        ConfigureMedia(model);
        ConfigureProviders(model);
    }

    private static void ConfigureCatalogue(ModelBuilder model)
    {
        model.Entity<ServiceCategory>(e =>
        {
            e.HasIndex(c => c.Slug).IsUnique();
            e.Property(c => c.Slug).HasMaxLength(80);
            e.Property(c => c.Name).HasMaxLength(100);
            e.Property(c => c.Description).HasMaxLength(500);
            e.HasOne<ServiceCategory>().WithMany()
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasData(CatalogueSeed.Categories());
        });

        model.Entity<ServiceArea>(e =>
        {
            e.HasIndex(a => new { a.Region, a.Name }).IsUnique();
            e.Property(a => a.Name).HasMaxLength(100);
            e.Property(a => a.Region).HasMaxLength(100);
            e.HasData(CatalogueSeed.Areas());
        });
    }

    private static void ConfigureMedia(ModelBuilder model)
    {
        model.Entity<MediaObject>(e =>
        {
            e.HasIndex(m => m.StorageKey).IsUnique();
            e.HasIndex(m => m.OwnerConsumerId);
            e.Property(m => m.StorageKey).HasMaxLength(64);
            e.Property(m => m.FileName).HasMaxLength(200);
            e.Property(m => m.ContentType).HasMaxLength(100);
            e.HasOne<Consumer>().WithMany()
                .HasForeignKey(m => m.OwnerConsumerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureProviders(ModelBuilder model)
    {
        model.Entity<ProviderAccount>(e =>
        {
            // One provider account per person in v1.
            e.HasIndex(a => a.OwnerConsumerId).IsUnique();
            e.Property(a => a.DisplayName).HasMaxLength(120);
            e.Property(a => a.LegalName).HasMaxLength(200);
            e.Property(a => a.Description).HasMaxLength(4000);
            e.Property(a => a.Email).HasMaxLength(254);
            e.Property(a => a.BusinessRegistrationNumber).HasMaxLength(50);
            e.Property(a => a.City).HasMaxLength(100);
            e.Property(a => a.PrimaryArea).HasMaxLength(100);
            e.Property(a => a.PremisesAddress).HasMaxLength(300);
            e.HasOne<Consumer>().WithMany()
                .HasForeignKey(a => a.OwnerConsumerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ProviderApplication>(e =>
        {
            e.HasIndex(a => a.ProviderAccountId).IsUnique();
            e.Property(a => a.MainSkill).HasMaxLength(100);
            e.Property(a => a.SecondarySkills).HasMaxLength(1000);
            e.Property(a => a.ToolsNotes).HasMaxLength(2000);
            e.Property(a => a.Certifications).HasMaxLength(2000);
            e.Property(a => a.ExperienceDescription).HasMaxLength(4000);
            e.Property(a => a.ReviewNote).HasMaxLength(2000);
            e.Property(a => a.RejectionReason).HasMaxLength(2000);
            e.Ignore(a => a.IsLocked);
            e.HasOne<ProviderAccount>().WithMany()
                .HasForeignKey(a => a.ProviderAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ProviderApplicationSubmission>(e =>
        {
            e.HasIndex(s => new { s.ProviderApplicationId, s.Version })
                .IsUnique()
                .HasDatabaseName("ix_provider_application_submissions_one_per_version");
            e.Property(s => s.Snapshot).HasColumnType("jsonb");
            e.HasOne<ProviderApplication>().WithMany()
                .HasForeignKey(s => s.ProviderApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ProviderOffering>(e =>
        {
            e.HasIndex(o => new { o.ProviderAccountId, o.ServiceCategoryId }).IsUnique();
            e.Property(o => o.CustomServiceName).HasMaxLength(100);
            e.Property(o => o.Description).HasMaxLength(1000);
            e.Property(o => o.UnitName).HasMaxLength(30);
            e.Property(o => o.FixedPrice).HasPrecision(12, 2);
            e.Property(o => o.StartingPrice).HasPrecision(12, 2);
            e.Property(o => o.InspectionFee).HasPrecision(12, 2);
            e.Property(o => o.UnitPrice).HasPrecision(12, 2);
            e.Ignore(o => o.HasRequiredPrice);
            e.HasOne<ProviderAccount>().WithMany()
                .HasForeignKey(o => o.ProviderAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ServiceCategory>().WithMany()
                .HasForeignKey(o => o.ServiceCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ProviderServiceArea>(e =>
        {
            e.HasIndex(a => new { a.ProviderAccountId, a.ServiceAreaId }).IsUnique();
            e.Property(a => a.MaximumTravelDistanceKm).HasPrecision(6, 1);
            e.HasOne<ProviderAccount>().WithMany()
                .HasForeignKey(a => a.ProviderAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ServiceArea>().WithMany()
                .HasForeignKey(a => a.ServiceAreaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ProviderAvailabilityRule>(e =>
        {
            e.HasIndex(r => new { r.ProviderAccountId, r.DayOfWeek }).IsUnique();
            e.HasOne<ProviderAccount>().WithMany()
                .HasForeignKey(r => r.ProviderAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<VerificationDocument>(e =>
        {
            e.ToTable("provider_verification_documents");
            e.HasIndex(d => new { d.ProviderAccountId, d.DocumentType });
            e.HasIndex(d => d.MediaObjectId).IsUnique();
            e.Property(d => d.StorageKey).HasMaxLength(64);
            e.Property(d => d.FileName).HasMaxLength(200);
            e.Property(d => d.ContentType).HasMaxLength(100);
            e.Property(d => d.ReviewNote).HasMaxLength(2000);
            e.HasOne<ProviderAccount>().WithMany()
                .HasForeignKey(d => d.ProviderAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<MediaObject>().WithMany()
                .HasForeignKey(d => d.MediaObjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PortfolioItem>(e =>
        {
            e.ToTable("provider_portfolio_items");
            e.HasIndex(p => p.ProviderAccountId);
            e.HasIndex(p => p.MediaObjectId).IsUnique();
            e.Property(p => p.Title).HasMaxLength(120);
            e.Property(p => p.Description).HasMaxLength(1000);
            e.Property(p => p.StorageKey).HasMaxLength(64);
            e.Property(p => p.ContentType).HasMaxLength(100);
            e.HasOne<ProviderAccount>().WithMany()
                .HasForeignKey(p => p.ProviderAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<MediaObject>().WithMany()
                .HasForeignKey(p => p.MediaObjectId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ServiceCategory>().WithMany()
                .HasForeignKey(p => p.ServiceCategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<StandardsAcceptance>(e =>
        {
            e.ToTable("provider_standards_acceptances");
            e.HasIndex(s => new { s.ProviderAccountId, s.StandardsVersion })
                .IsUnique()
                .HasDatabaseName("ix_provider_standards_acceptances_one_per_version");
            e.Property(s => s.StandardsVersion).HasMaxLength(32);
            e.HasOne<ProviderAccount>().WithMany()
                .HasForeignKey(s => s.ProviderAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Consumer>().WithMany()
                .HasForeignKey(s => s.AcceptedByConsumerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayoutMethod>(e =>
        {
            e.HasIndex(m => new { m.ProviderAccountId, m.AccountNumberFingerprint })
                .IsUnique()
                .HasFilter("archived_at IS NULL")
                .HasDatabaseName("ix_payout_methods_one_per_number");
            e.HasIndex(m => m.ProviderAccountId)
                .IsUnique()
                .HasFilter("is_default AND archived_at IS NULL")
                .HasDatabaseName("ix_payout_methods_one_default");
            e.Property(m => m.ProviderName).HasMaxLength(100);
            e.Property(m => m.AccountName).HasMaxLength(120);
            e.Property(m => m.AccountNumberFingerprint).HasMaxLength(64);
            e.Property(m => m.MaskedAccountNumber).HasMaxLength(40);
            e.HasOne<ProviderAccount>().WithMany()
                .HasForeignKey(m => m.ProviderAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }
}

/// <summary>Lets <c>dotnet ef</c> build the model without starting the app or reading secrets.</summary>
public sealed class GridDbContextDesignFactory : IDesignTimeDbContextFactory<GridDbContext>
{
    public GridDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GridDbContext>()
            .UseNpgsql("Host=localhost;Database=grid")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new GridDbContext(options, TimeProvider.System);
    }
}
