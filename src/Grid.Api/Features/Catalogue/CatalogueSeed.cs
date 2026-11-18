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
    public static readonly Guid AcServicingId = IdFor("category:ac-servicing");
    public static readonly Guid PestControlId = IdFor("category:pest-control");
    public static readonly Guid PlumbingId = IdFor("category:plumbing");
    public static readonly Guid ElectricalId = IdFor("category:electrical");

    private const string CleaningSection = "Cleaning & laundry";
    private const string RepairsSection = "Repairs & maintenance";

    private sealed record Service(
        string Slug, string Name, string Description, PricingMode Pricing, BookingMode Booking, string Section,
        bool Launched, string[] Children);

    private static readonly Service[] Services =
    [
        new("cleaning", "Home cleaning", "Homes and apartments: regular, deep and move-out cleans.", PricingMode.PerUnit,
            BookingMode.FixedPrice, CleaningSection, true,
            ["Standard cleaning", "Deep cleaning", "Move-in / move-out cleaning", "After-party cleaning",
             "Sofa & carpet cleaning", "Window cleaning"]),
        new("laundry", "Laundry", "Wash, iron and dry cleaning, with pickup and delivery.", PricingMode.StartingFrom,
            BookingMode.FixedPrice, CleaningSection, true,
            ["Wash & fold", "Wash & iron", "Ironing only", "Dry cleaning", "Duvets & bulky items", "Pickup & delivery"]),
        new("ac-servicing", "AC servicing", "Split and window units: servicing, deep cleans, gas and repairs.", PricingMode.PerUnit,
            BookingMode.FixedPrice, RepairsSection, true,
            ["General servicing", "Deep foam cleaning", "Gas top-up", "Fault diagnosis", "Installation"]),
        new("pest-control", "Pest control", "Fumigation for mosquitoes, cockroaches, rodents and termites.", PricingMode.FixedPrice,
            BookingMode.FixedPrice, RepairsSection, true,
            ["General fumigation", "Rodent control", "Termite treatment", "Bed bugs"]),
        new("plumbing", "Plumbing", "Leaks, blocked drains, taps, toilets, water heaters and pumps.", PricingMode.InspectionThenQuote,
            BookingMode.CallOutThenQuote, RepairsSection, false,
            ["Leaks", "Blocked drains", "Toilets", "Taps & showers", "Water heaters", "Tanks & pumps"]),
        new("electrical", "Electrical", "Sockets, switches, lighting, breakers and wiring faults.", PricingMode.InspectionThenQuote,
            BookingMode.CallOutThenQuote, RepairsSection, false,
            ["Sockets & switches", "Lighting", "Breakers & fuses", "Wiring faults", "Appliance connection"]),
    ];

    /// <summary>
    /// GRID's launch prices in GH₵. These are proposals to check with local providers before
    /// launch; changing a number here and adding a migration is all a price change takes.
    /// </summary>
    private sealed record Package(
        string Service, string Slug, string Group, string Name, decimal Price, int Minutes, string? Unit = null, int Max = 1,
        string? Summary = null, string[]? Includes = null, string[]? Excludes = null);

    private static readonly string[] CleanIncludes =
        ["Dusting and wiping of all surfaces", "Floors swept and mopped", "Kitchen counters, sink and hob", "Bathrooms and toilets", "Bins emptied"];
    private static readonly string[] DeepIncludes =
        ["Everything in a standard clean", "Inside cupboards and appliances", "Tiles, grout and fittings scrubbed", "Doors, frames and switches", "Windows from inside"];
    private static readonly string[] MoveIncludes =
        ["Everything in a deep clean", "Empty-home walls spot-cleaned", "Inside wardrobes and drawers", "Balconies and storerooms"];
    private static readonly string[] CleanExcludes = ["Laundry and ironing", "Outside windows above ground floor", "Moving heavy furniture"];

    private static readonly Package[] Packages =
    [
        new("cleaning", "standard-1-bed", "Standard clean", "1 bedroom", 180, 120, Includes: CleanIncludes, Excludes: CleanExcludes,
            Summary: "Keeps a home in shape. Supplies included."),
        new("cleaning", "standard-2-bed", "Standard clean", "2 bedrooms", 250, 180, Includes: CleanIncludes, Excludes: CleanExcludes,
            Summary: "Keeps a home in shape. Supplies included."),
        new("cleaning", "standard-3-bed", "Standard clean", "3 bedrooms", 330, 240, Includes: CleanIncludes, Excludes: CleanExcludes,
            Summary: "Keeps a home in shape. Supplies included."),
        new("cleaning", "standard-4-bed", "Standard clean", "4 bedrooms", 420, 300, Includes: CleanIncludes, Excludes: CleanExcludes,
            Summary: "Keeps a home in shape. Supplies included."),
        new("cleaning", "deep-1-bed", "Deep clean", "1 bedroom", 320, 240, Includes: DeepIncludes, Excludes: CleanExcludes,
            Summary: "Top to bottom, inside and out. Two cleaners for larger homes."),
        new("cleaning", "deep-2-bed", "Deep clean", "2 bedrooms", 450, 300, Includes: DeepIncludes, Excludes: CleanExcludes,
            Summary: "Top to bottom, inside and out. Two cleaners for larger homes."),
        new("cleaning", "deep-3-bed", "Deep clean", "3 bedrooms", 590, 360, Includes: DeepIncludes, Excludes: CleanExcludes,
            Summary: "Top to bottom, inside and out. Two cleaners for larger homes."),
        new("cleaning", "deep-4-bed", "Deep clean", "4 bedrooms", 760, 420, Includes: DeepIncludes, Excludes: CleanExcludes,
            Summary: "Top to bottom, inside and out. Two cleaners for larger homes."),
        new("cleaning", "move-1-bed", "Move-in / move-out", "1 bedroom", 360, 300, Includes: MoveIncludes, Excludes: CleanExcludes,
            Summary: "For an empty home, before you move in or after you move out."),
        new("cleaning", "move-2-bed", "Move-in / move-out", "2 bedrooms", 500, 360, Includes: MoveIncludes, Excludes: CleanExcludes,
            Summary: "For an empty home, before you move in or after you move out."),
        new("cleaning", "move-3-bed", "Move-in / move-out", "3 bedrooms", 660, 420, Includes: MoveIncludes, Excludes: CleanExcludes,
            Summary: "For an empty home, before you move in or after you move out."),
        new("cleaning", "move-4-bed", "Move-in / move-out", "4 bedrooms", 840, 480, Includes: MoveIncludes, Excludes: CleanExcludes,
            Summary: "For an empty home, before you move in or after you move out."),
        new("cleaning", "sofa-seat", "Sofa cleaning", "Sofa cleaning", 60, 30, "seat", 12,
            "Shampoo and extraction for fabric sofas, priced per seat.",
            ["Vacuuming", "Stain treatment", "Shampoo and extraction"], ["Leather restoration"]),

        new("laundry", "wash-fold", "Wash & fold", "Wash & fold", 60, 1440, "bag", 10,
            "Washed, dried and folded. Collected and returned within 48 hours.",
            ["Collection and return", "Washing and drying", "Folding"], ["Ironing", "Dry-clean-only items"]),
        new("laundry", "wash-iron", "Wash & iron", "Wash & iron", 90, 1440, "bag", 10,
            "Washed, dried and ironed. Shirts on hangers.",
            ["Collection and return", "Washing and drying", "Ironing", "Shirts on hangers"], ["Dry-clean-only items"]),
        new("laundry", "duvet", "Duvets & bulky items", "Duvets & bulky items", 50, 1440, "item", 10,
            "Duvets, blankets and curtains.", ["Collection and return", "Washing and drying"]),
        new("laundry", "dry-clean-suit", "Dry cleaning", "Dry cleaning", 70, 2880, "garment", 20,
            "Suits, kaftans, dresses and anything marked dry clean.", ["Collection and return", "Dry cleaning", "Pressing"]),

        new("ac-servicing", "general-service", "General service", "General service", 250, 60, "unit", 6,
            "Filters, coils and drain cleaned; gas pressure checked.",
            ["Filter wash", "Indoor and outdoor coil cleaning", "Drain line flush", "Gas pressure and leak check"],
            ["Gas top-up", "Spare parts"]),
        new("ac-servicing", "deep-foam", "Deep foam clean", "Deep foam clean", 350, 90, "unit", 6,
            "For units that smell, drip or haven't been cleaned in a year.",
            ["Everything in a general service", "Foam-jet clean of the indoor unit", "Blower and fan cleaned"], ["Gas top-up", "Spare parts"]),
        new("ac-servicing", "gas-top-up", "Gas top-up", "Gas top-up", 450, 60, "unit", 6,
            "Refrigerant topped up after a leak check.", ["Leak check", "Refrigerant top-up", "Cooling test"], ["Leak repair", "Spare parts"]),
        new("ac-servicing", "not-cooling", "Not cooling", "Diagnosis", 150, 45,
            Summary: "A technician finds the fault and tells you the fix and price before doing it.",
            Includes: ["Fault diagnosis", "A written quote for the fix"], Excludes: ["The repair itself", "Spare parts"]),

        new("pest-control", "fumigation-small", "General fumigation", "Studio to 2 bedrooms", 400, 90,
            Summary: "Mosquitoes, cockroaches, ants and flies. Licensed operators and approved products.",
            Includes: ["Indoor spraying", "Compound perimeter spray", "Gel bait in the kitchen", "Re-treatment within 30 days if pests return"],
            Excludes: ["Termites", "Rodents"]),
        new("pest-control", "fumigation-large", "General fumigation", "3 to 4 bedrooms", 550, 150,
            Summary: "Mosquitoes, cockroaches, ants and flies. Licensed operators and approved products.",
            Includes: ["Indoor spraying", "Compound perimeter spray", "Gel bait in the kitchen", "Re-treatment within 30 days if pests return"],
            Excludes: ["Termites", "Rodents"]),
        new("pest-control", "fumigation-xl", "General fumigation", "5 bedrooms or more", 750, 210,
            Summary: "Mosquitoes, cockroaches, ants and flies. Licensed operators and approved products.",
            Includes: ["Indoor spraying", "Compound perimeter spray", "Gel bait in the kitchen", "Re-treatment within 30 days if pests return"],
            Excludes: ["Termites", "Rodents"]),
        new("pest-control", "rodents", "Rodent control", "Rodent control", 450, 90,
            Summary: "Baiting and traps, with a follow-up visit.", Includes: ["Inspection", "Bait stations and traps", "One follow-up visit"]),
        new("pest-control", "termite-inspection", "Termite inspection", "Termite inspection", 200, 60,
            Summary: "Finds the extent of the damage and quotes the treatment.", Includes: ["Inspection", "A written treatment quote"],
            Excludes: ["The treatment itself"]),

        new("plumbing", "call-out", "Call-out & diagnosis", "Call-out & diagnosis", 120, 60,
            Summary: "A plumber comes, finds the problem and quotes the fix. You approve the price before any work.",
            Includes: ["Visit and diagnosis", "A quote in the app"], Excludes: ["Repairs and parts, until you approve the quote"]),
        new("electrical", "call-out", "Call-out & diagnosis", "Call-out & diagnosis", 150, 60,
            Summary: "A certified electrician finds the fault and quotes the fix. You approve the price before any work.",
            Includes: ["Visit and diagnosis", "A quote in the app"], Excludes: ["Repairs and parts, until you approve the quote"]),
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
                DefaultPricingMode = service.Pricing, BookingMode = service.Booking, Section = service.Section,
                IsLaunched = service.Launched, SortOrder = ++order, CreatedAt = SeededAt, UpdatedAt = SeededAt,
            };

            var childOrder = 0;
            foreach (var child in service.Children)
            {
                var slug = $"{service.Slug}-{Slugify(child)}";
                yield return new ServiceCategory
                {
                    Id = IdFor($"category:{slug}"), ParentCategoryId = parentId, Slug = slug, Name = child,
                    DefaultPricingMode = service.Pricing, BookingMode = service.Booking, Section = service.Section,
                    IsLaunched = service.Launched, SortOrder = ++childOrder, CreatedAt = SeededAt, UpdatedAt = SeededAt,
                };
            }
        }
    }

    public static IEnumerable<ServicePackage> ServicePackages()
    {
        var order = 0;
        foreach (var p in Packages)
        {
            var slug = $"{p.Service}-{p.Slug}";
            yield return new ServicePackage
            {
                Id = IdFor($"package:{slug}"), ServiceCategoryId = IdFor($"category:{p.Service}"), Slug = slug,
                Group = p.Group, Name = p.Name, Summary = p.Summary, Includes = p.Includes is null ? null : string.Join('\n', p.Includes),
                Excludes = p.Excludes is null ? null : string.Join('\n', p.Excludes),
                Price = p.Price, DurationMinutes = p.Minutes, UnitLabel = p.Unit, MaxQuantity = p.Max, SortOrder = ++order,
                CreatedAt = SeededAt, UpdatedAt = SeededAt,
            };
        }
    }

    /// <summary>
    /// Town centres are approximate (to about a kilometre) and the radius is how far the name
    /// is commonly used, so neighbouring areas overlap. Correct them here as real jobs show where
    /// the edges are; ids come from the names, so changing a coordinate never changes an id.
    /// </summary>
    private static readonly (string Region, (string Name, double Lat, double Lng, double RadiusKm)[] Towns)[] Regions =
    [
        ("Greater Accra",
        [
            ("East Legon", 5.6350, -0.1570, 3.5), ("Spintex", 5.6360, -0.1010, 4), ("Adenta", 5.7090, -0.1670, 3.5),
            ("Madina", 5.6830, -0.1660, 3), ("Tema", 5.6698, -0.0166, 6), ("Osu", 5.5560, -0.1820, 2),
            ("Labone", 5.5640, -0.1730, 2), ("Cantonments", 5.5790, -0.1740, 2), ("Airport Residential", 5.6050, -0.1830, 2.5),
            ("Dansoman", 5.5390, -0.2600, 3.5), ("Achimota", 5.6140, -0.2290, 3), ("Lapaz", 5.6070, -0.2500, 2.5),
            ("Dome", 5.6500, -0.2360, 3), ("Ashaley Botwe", 5.6850, -0.1370, 3), ("Teshie", 5.5830, -0.1050, 3),
            ("Nungua", 5.6010, -0.0770, 3), ("Kasoa", 5.5340, -0.4170, 5), ("Weija", 5.5560, -0.3340, 4),
            ("Oyarifa", 5.7400, -0.1750, 3.5), ("Pokuase", 5.6960, -0.2830, 4),
        ]),
        ("Ashanti",
        [
            ("Ahodwo", 6.6660, -1.6270, 2.5), ("Asokwa", 6.6730, -1.6050, 2.5), ("Bantama", 6.7030, -1.6370, 2.5),
            ("Suame", 6.7170, -1.6280, 2.5), ("Tafo", 6.7330, -1.6130, 3), ("Ejisu", 6.7210, -1.4700, 5),
            ("Santasi", 6.6650, -1.6500, 3), ("Kwadaso", 6.6900, -1.6560, 3), ("Atonsu", 6.6540, -1.5950, 3),
            ("Obuasi", 6.2000, -1.6667, 6),
        ]),
        ("Western",
        [
            ("Takoradi", 4.8970, -1.7600, 4), ("Sekondi", 4.9340, -1.7040, 4), ("Anaji", 4.9190, -1.7780, 3),
            ("Airport Ridge", 4.8960, -1.7740, 2), ("Effia", 4.9230, -1.7570, 3), ("Tarkwa", 5.3010, -1.9930, 6),
        ]),
        ("Central",
        [
            ("Cape Coast", 5.1053, -1.2466, 6), ("Elmina", 5.0847, -1.3509, 4), ("Mankessim", 5.2710, -1.0170, 4),
            ("Winneba", 5.3510, -0.6230, 5), ("Swedru", 5.5370, -0.7000, 5),
        ]),
        ("Eastern",
        [
            ("Koforidua", 6.0940, -0.2590, 6), ("Nsawam", 5.8080, -0.3500, 5), ("Suhum", 6.0400, -0.4500, 4),
            ("Akosombo", 6.2960, 0.0590, 5), ("Nkawkaw", 6.5510, -0.7660, 5),
        ]),
        ("Northern",
        [
            ("Tamale", 9.4035, -0.8423, 7), ("Sagnarigu", 9.4350, -0.8700, 4), ("Savelugu", 9.6240, -0.8250, 5),
            ("Yendi", 9.4420, -0.0100, 5),
        ]),
    ];

    public static IEnumerable<ServiceArea> Areas()
    {
        var order = 0;
        foreach (var (region, towns) in Regions)
        foreach (var town in towns)
        {
            yield return new ServiceArea
            {
                Id = IdFor($"area:{region}:{town.Name}"), Name = town.Name, Region = region, SortOrder = ++order,
                Latitude = town.Lat, Longitude = town.Lng, RadiusKm = town.RadiusKm,
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
