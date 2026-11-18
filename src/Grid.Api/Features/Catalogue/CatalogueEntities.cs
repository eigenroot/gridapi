using Grid.Api.Common;
using Grid.Api.Data;

namespace Grid.Api.Features.Catalogue;

/// <summary>How a provider prices an offering. Kept for the provider app; GRID's own package prices decide what customers pay.</summary>
public enum PricingMode { FixedPrice, StartingFrom, PerUnit, InspectionThenQuote, QuoteBeforeVisit }

/// <summary>
/// How a customer books a service. <c>FixedPrice</c>: they buy packages at GRID's price.
/// <c>CallOutThenQuote</c>: they pay a call-out, and the work is quoted on site.
/// </summary>
public enum BookingMode { FixedPrice, CallOutThenQuote }

/// <summary>
/// A service GRID offers. Top-level rows are what a provider is approved to deliver and what
/// requests are matched on; their children are the skills listed under them in GRID Pro.
/// </summary>
public sealed class ServiceCategory : Entity
{
    public Guid? ParentCategoryId { get; set; }

    /// <summary>Stable identifier the apps key presentation on, e.g. <c>cleaning</c>.</summary>
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>The pricing a new offering in this category starts from.</summary>
    public PricingMode DefaultPricingMode { get; set; }
    public BookingMode BookingMode { get; set; }

    /// <summary>The group the consumer app shows it under, e.g. "Cleaning &amp; laundry".</summary>
    public string? Section { get; set; }

    /// <summary>
    /// Whether customers can book it. A category can exist (and providers can sign up for it)
    /// before it is launched to customers.
    /// </summary>
    public bool IsLaunched { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Something a customer buys, at GRID's price: "Standard clean · 2 bedrooms", "General service".
/// Packages in the same <see cref="Group"/> are alternatives (home sizes); a quantity multiplies
/// the price where <see cref="MaxQuantity"/> allows it (bags, AC units).
/// </summary>
public sealed class ServicePackage : Entity
{
    public Guid ServiceCategoryId { get; set; }
    public required string Slug { get; set; }

    /// <summary>The kind of job, e.g. "Standard clean". Equal to <see cref="Name"/> when there are no variants.</summary>
    public required string Group { get; set; }
    public required string Name { get; set; }
    public string? Summary { get; set; }
    /// <summary>What the price covers, one item per line. Text rather than an array so seeded rows compare by value.</summary>
    public string? Includes { get; set; }

    /// <summary>What it doesn't, one item per line.</summary>
    public string? Excludes { get; set; }

    public static List<string> Lines(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public decimal Price { get; set; }

    /// <summary>Roughly how long one takes, for scheduling and for the customer.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>What one of it is, e.g. "bag", "unit". Null for a whole job.</summary>
    public string? UnitLabel { get; set; }
    public int MaxQuantity { get; set; } = 1;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A town or neighbourhood a provider can serve, grouped by region. Each one is a circle on the
/// map (a centre and a radius), which is what turns a customer's location into an area and lets
/// providers be ranked by distance.
/// </summary>
public sealed class ServiceArea : Entity
{
    public required string Name { get; set; }
    public required string Region { get; set; }

    /// <summary>The centre of the area, in decimal degrees.</summary>
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>How far from the centre the area reaches.</summary>
    public double RadiusKm { get; set; }

    public GeoPoint Centre => new(Latitude, Longitude);
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
