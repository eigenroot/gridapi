using Grid.Api.Common;
using Grid.Api.Features.Consumers;
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

    public static async Task RunAsync(GridDbContext db)
    {
        if (await db.Consumers.AnyAsync()) return;

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
}
