using Grid.Api.Common;
using Grid.Api.Features.Catalogue;
using Grid.Api.Features.Consumers;
using Grid.Api.Features.Providers;
using Grid.Api.Features.Properties;
using Grid.Api.Features.UtilityAccounts;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Data;

/// <summary>
/// A signed-up consumer with a home and saved accounts, so the apps have something to show
/// locally. Runs only in Development, and only into an empty database.
/// </summary>
public static class DevelopmentSeed
{
    public const string DemoPhone = "233240000001";

    /// <summary>An approved cleaner and launderer in East Legon, online and ready for offers.</summary>
    public const string DemoProviderPhone = "233240000002";

    public static async Task RunAsync(GridDbContext db, PayoutProtector protector)
    {
        await SeedProviderAsync(db, protector);
        if (await db.Consumers.AnyAsync(c => c.PhoneNumber == DemoPhone)) return;

        var ama = new Consumer
        {
            PhoneNumber = DemoPhone,
            PhoneNetwork = PhoneNumbers.GuessNetwork(DemoPhone),
            FirstName = "Ama",
            LastName = "Mensah",
            Email = "ama@example.com",
            ProfileCompletedAt = DateTimeOffset.UtcNow,
            FirstSeenApp = AppKind.Consumer,
        };
        db.Consumers.Add(ama);
        await db.SaveChangesAsync();

        var home = new Property
        {
            OwnerConsumerId = ama.Id,
            Nickname = "Home",
            Kind = PropertyKind.House,
            Area = "East Legon",
            City = "Accra",
            Address = "12 Lagos Avenue",
            GhanaPostGps = "GA-492-7710",
            Latitude = 5.6362,
            Longitude = -0.1583,
            ServiceAreaId = CatalogueSeed.IdFor("area:Greater Accra:East Legon"),
        };
        db.Properties.Add(home);
        await db.SaveChangesAsync();
        ama.DefaultPropertyId = home.Id;

        db.UtilityAccounts.AddRange(
            new UtilityAccount
            {
                OwnerConsumerId = ama.Id, Type = UtilityType.Ecg, AccountNumber = "0123456789012",
                Nickname = "Home meter", PropertyId = home.Id, IsDefault = true,
            },
            new UtilityAccount
            {
                OwnerConsumerId = ama.Id, Type = UtilityType.Water, AccountNumber = "40123456",
                Nickname = "Home water", PropertyId = home.Id, IsDefault = true,
            },
            new UtilityAccount
            {
                OwnerConsumerId = ama.Id, Type = UtilityType.Airtime, AccountNumber = DemoPhone,
                Nickname = "My number", IsDefault = true,
            });
        await db.SaveChangesAsync();
    }

    /// <summary>An approved AC and pest control technician in East Legon, online and ready for offers.</summary>
    public const string DemoTechnicianPhone = "233240000003";

    private sealed record SeedProvider(
        string Phone, string FirstName, string LastName, string DisplayName, string Description,
        double Lat, double Lng, Guid[] Services, string[] Areas);

    private static readonly SeedProvider[] Providers =
    [
        new(DemoProviderPhone, "Efua", "Asante", "Fresh Start Cleaning & Laundry",
            "Homes and laundry across East Legon, Spintex and Adenta since 2021.", 5.6410, -0.1520,
            [CatalogueSeed.CleaningId, CatalogueSeed.LaundryId], ["East Legon", "Spintex", "Adenta", "Airport Residential"]),
        new(DemoTechnicianPhone, "Kwesi", "Boateng", "CoolBreeze Technical Services",
            "AC servicing and licensed fumigation in East Legon, Airport and Cantonments.", 5.6290, -0.1650,
            [CatalogueSeed.AcServicingId, CatalogueSeed.PestControlId], ["East Legon", "Airport Residential", "Cantonments", "Madina"]),
    ];

    private static async Task SeedProviderAsync(GridDbContext db, PayoutProtector protector)
    {
        foreach (var seed in Providers) await SeedProviderAsync(db, protector, seed);
    }

    private static async Task SeedProviderAsync(GridDbContext db, PayoutProtector protector, SeedProvider seed)
    {
        if (await db.Consumers.AnyAsync(c => c.PhoneNumber == seed.Phone)) return;

        var now = DateTimeOffset.UtcNow;
        var owner = new Consumer
        {
            PhoneNumber = seed.Phone,
            PhoneNetwork = PhoneNumbers.GuessNetwork(seed.Phone),
            FirstName = seed.FirstName,
            LastName = seed.LastName,
            ProfileCompletedAt = now,
            FirstSeenApp = AppKind.Pro,
        };
        var account = new ProviderAccount
        {
            OwnerConsumerId = owner.Id,
            AccountType = ProviderAccountType.Business,
            DisplayName = seed.DisplayName,
            Description = seed.Description,
            City = "Accra",
            PrimaryArea = seed.Areas[0],
            YearsOfExperience = 5,
            BaseLatitude = seed.Lat,
            BaseLongitude = seed.Lng,
            LocationUpdatedAt = now,
            TravelRadiusKm = 15,
            VerificationStatus = ProviderVerificationStatus.Approved,
            OperatingStatus = OperatingStatus.Online,
        };
        db.Consumers.Add(owner);
        db.ProviderAccounts.Add(account);
        db.ProviderApplications.Add(new ProviderApplication
        {
            ProviderAccountId = account.Id, Status = ApplicationStatus.Approved, SubmittedAt = now, ReviewedAt = now,
        });
        foreach (var service in seed.Services)
        {
            db.ProviderOfferings.Add(new ProviderOffering
            {
                ProviderAccountId = account.Id, ServiceCategoryId = service, PricingMode = PricingMode.FixedPrice, SupportsSameDay = true,
            });
        }
        foreach (var town in seed.Areas)
        {
            db.ProviderServiceAreas.Add(new ProviderServiceArea
            {
                ProviderAccountId = account.Id, ServiceAreaId = CatalogueSeed.IdFor($"area:Greater Accra:{town}"), SupportsSameDay = true,
            });
        }
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            db.ProviderAvailabilityRules.Add(new ProviderAvailabilityRule
            {
                ProviderAccountId = account.Id, DayOfWeek = day, StartTime = new TimeOnly(7, 0), EndTime = new TimeOnly(18, 0),
                IsAvailable = day != DayOfWeek.Sunday,
            });
        }
        db.PayoutMethods.Add(new PayoutMethod
        {
            ProviderAccountId = account.Id,
            MethodType = PayoutMethodType.MobileMoney,
            ProviderName = "MTN Mobile Money",
            AccountName = $"{seed.FirstName} {seed.LastName}",
            AccountNumberEncrypted = protector.Encrypt(seed.Phone),
            AccountNumberFingerprint = protector.Fingerprint(PayoutMethodType.MobileMoney, seed.Phone),
            MaskedAccountNumber = PayoutProtector.Mask(seed.Phone),
            IsDefault = true,
        });
        await db.SaveChangesAsync();
    }
}
