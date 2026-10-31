using Grid.Api.Data;

namespace Grid.Api.Features.Catalogue;

/// <summary>How a provider prices an offering. The provider app asks about three of these.</summary>
public enum PricingMode { FixedPrice, StartingFrom, PerUnit, InspectionThenQuote, QuoteBeforeVisit }

/// <summary>
/// A service GRID offers. Top-level rows are what a provider signs up to deliver; their children
/// are the sub-services shown under them. Launches with cleaning and laundry only.
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
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A town or neighbourhood a provider can serve, grouped by region.</summary>
public sealed class ServiceArea : Entity
{
    public required string Name { get; set; }
    public required string Region { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
