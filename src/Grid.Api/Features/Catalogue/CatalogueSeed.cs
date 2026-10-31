using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Grid.Api.Features.Catalogue;

/// <summary>
/// The launch catalogue, written into the database by migrations so every environment has it.
/// Ids are derived from names, so regenerating a migration never changes them. Names match the
/// GRID Pro bundled Ghana configuration, which is what the app matches areas on.
/// </summary>
public static partial class CatalogueSeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

    public static readonly Guid CleaningId = IdFor("category:cleaning");
    public static readonly Guid LaundryId = IdFor("category:laundry");

    private static readonly (string Slug, string Name, string Description, PricingMode Pricing, string[] Children)[] Services =
    [
        ("cleaning", "Home cleaning", "Homes and apartments: regular, deep and move-out cleans.", PricingMode.PerUnit,
        [
            "Standard cleaning", "Deep cleaning", "Move-in / move-out cleaning", "After-party cleaning",
            "Sofa & carpet cleaning", "Window cleaning",
        ]),
        ("laundry", "Laundry", "Wash, iron and dry cleaning, with pickup and delivery.", PricingMode.StartingFrom,
        [
            "Wash & fold", "Wash & iron", "Ironing only", "Dry cleaning", "Duvets & bulky items", "Pickup & delivery",
        ]),
    ];

    private static readonly (string Region, string[] Towns)[] Regions =
    [
        ("Greater Accra",
        [
            "East Legon", "Spintex", "Adenta", "Madina", "Tema", "Osu", "Labone", "Cantonments",
            "Airport Residential", "Dansoman", "Achimota", "Lapaz", "Dome", "Ashaley Botwe", "Teshie", "Nungua",
            "Kasoa", "Weija", "Oyarifa", "Pokuase",
        ]),
        ("Ashanti", ["Ahodwo", "Asokwa", "Bantama", "Suame", "Tafo", "Ejisu", "Santasi", "Kwadaso", "Atonsu", "Obuasi"]),
        ("Western", ["Takoradi", "Sekondi", "Anaji", "Airport Ridge", "Effia", "Tarkwa"]),
        ("Central", ["Cape Coast", "Elmina", "Mankessim", "Winneba", "Swedru"]),
        ("Eastern", ["Koforidua", "Nsawam", "Suhum", "Akosombo", "Nkawkaw"]),
        ("Northern", ["Tamale", "Sagnarigu", "Savelugu", "Yendi"]),
    ];

    public static IEnumerable<ServiceCategory> Categories()
    {
        var order = 0;
        foreach (var service in Services)
        {
            var parentId = IdFor($"category:{service.Slug}");
            yield return new ServiceCategory
            {
                Id = parentId, Slug = service.Slug, Name = service.Name, Description = service.Description,
                DefaultPricingMode = service.Pricing, SortOrder = ++order, CreatedAt = SeededAt, UpdatedAt = SeededAt,
            };

            var childOrder = 0;
            foreach (var child in service.Children)
            {
                var slug = $"{service.Slug}-{Slugify(child)}";
                yield return new ServiceCategory
                {
                    Id = IdFor($"category:{slug}"), ParentCategoryId = parentId, Slug = slug, Name = child,
                    DefaultPricingMode = service.Pricing, SortOrder = ++childOrder, CreatedAt = SeededAt, UpdatedAt = SeededAt,
                };
            }
        }
    }

    public static IEnumerable<ServiceArea> Areas()
    {
        var order = 0;
        foreach (var (region, towns) in Regions)
        foreach (var town in towns)
        {
            yield return new ServiceArea
            {
                Id = IdFor($"area:{region}:{town}"), Name = town, Region = region, SortOrder = ++order,
                CreatedAt = SeededAt, UpdatedAt = SeededAt,
            };
        }
    }

    /// <summary>A stable id from a name, so seed rows keep their ids across migrations.</summary>
    internal static Guid IdFor(string name) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes("grid-catalogue:" + name)));

    private static string Slugify(string name) => NonAlphanumeric().Replace(name.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
