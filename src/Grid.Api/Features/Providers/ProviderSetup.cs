using Grid.Api.Data;
using Grid.Api.Features.Catalogue;

namespace Grid.Api.Features.Providers;

/// <summary>
/// A service the provider offers and how they price it; one per top-level category. Prices are
/// optional while the application is a draft; readiness reports a missing one.
/// </summary>
public sealed class ProviderOffering : Entity
{
    public Guid ProviderAccountId { get; set; }
    public Guid ServiceCategoryId { get; set; }
    public string? CustomServiceName { get; set; }
    public string? Description { get; set; }

    public PricingMode PricingMode { get; set; }
    public decimal? FixedPrice { get; set; }
    public decimal? StartingPrice { get; set; }
    public decimal? InspectionFee { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? UnitName { get; set; }

    public bool SupportsEmergency { get; set; }
    public bool SupportsSameDay { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>The price this offering's mode needs, or true when the mode needs none up front.</summary>
    public bool HasRequiredPrice => PricingMode switch
    {
        PricingMode.FixedPrice => FixedPrice is not null,
        PricingMode.StartingFrom => StartingPrice is not null,
        PricingMode.PerUnit => UnitPrice is not null,
        PricingMode.InspectionThenQuote => InspectionFee is not null,
        _ => true,
    };
}

/// <summary>An area the provider is willing to work in. Enforcement arrives with job offers.</summary>
public sealed class ProviderServiceArea : Entity
{
    public Guid ProviderAccountId { get; set; }
    public Guid ServiceAreaId { get; set; }
    public decimal? MaximumTravelDistanceKm { get; set; }
    public bool SupportsEmergency { get; set; }
    public bool SupportsSameDay { get; set; }
}

/// <summary>One day of the provider's weekly working pattern, in Africa/Accra local time.</summary>
public sealed class ProviderAvailabilityRule : Entity
{
    public Guid ProviderAccountId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsAvailable { get; set; }
}
