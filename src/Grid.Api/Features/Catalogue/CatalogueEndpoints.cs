using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Marketplace;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Catalogue;

public sealed record ServiceCategoryResponse(
    Guid Id,
    Guid? ParentCategoryId,
    string Slug,
    string Name,
    string? Description,
    PricingMode DefaultPricingMode,
    int SortOrder,
    BookingMode BookingMode,
    string? Section,
    bool IsLaunched);

public sealed record PackageResponse(
    Guid Id,
    string Slug,
    string Group,
    string Name,
    string? Summary,
    List<string> Includes,
    List<string> Excludes,
    decimal Price,
    int DurationMinutes,
    string? UnitLabel,
    int MaxQuantity)
{
    public static PackageResponse From(ServicePackage p) =>
        new(p.Id, p.Slug, p.Group, p.Name, p.Summary, ServicePackage.Lines(p.Includes), ServicePackage.Lines(p.Excludes), p.Price, p.DurationMinutes, p.UnitLabel, p.MaxQuantity);
}

/// <summary>A bookable service: what it is, how it is booked, and its packages at GRID's prices.</summary>
public sealed record CatalogueServiceResponse(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    BookingMode BookingMode,
    decimal FromPrice,
    List<string> Skills,
    List<PackageResponse> Packages);

public sealed record CatalogueSectionResponse(string Name, List<CatalogueServiceResponse> Services);

/// <param name="FeePercent">GRID's share of each price; a provider earns the rest. GRID Pro shows it as earnings.</param>
public sealed record CatalogueResponse(List<CatalogueSectionResponse> Sections, decimal FeePercent);

public sealed record ServiceAreaResponse(Guid Id, string Name, string Region, double Latitude, double Longitude, double RadiusKm)
{
    public static ServiceAreaResponse From(ServiceArea a) => new(a.Id, a.Name, a.Region, a.Latitude, a.Longitude, a.RadiusKm);
}

/// <summary>The area a point falls in. <c>served</c> is false when the nearest area is too far to count.</summary>
public sealed record LocatedAreaResponse(bool Served, ServiceAreaResponse? Area, double? DistanceKm);

/// <summary>Public reference data. GRID Pro loads it before sign-in, so neither read needs a token.</summary>
public static class CatalogueEndpoints
{
    public static void MapCatalogueEndpoints(this IEndpointRouteBuilder api)
    {
        var catalogue = api.MapGroup("").WithTags("Catalogue").AllowAnonymous();

        catalogue.MapGet("/service-catalogue", Catalogue);
        catalogue.MapGet("/service-categories", Categories);
        catalogue.MapGet("/service-areas", Areas);
        catalogue.MapGet("/service-areas/locate", Locate);
    }

    private static async Task<EnvelopeResult<List<ServiceCategoryResponse>>> Categories(GridDbContext db, CancellationToken ct) =>
        Envelope.Ok(await db.ServiceCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.ParentCategoryId != null).ThenBy(c => c.SortOrder)
            .Select(c => new ServiceCategoryResponse(
                c.Id, c.ParentCategoryId, c.Slug, c.Name, c.Description, c.DefaultPricingMode, c.SortOrder,
                c.BookingMode, c.Section, c.IsLaunched))
            .ToListAsync(ct));

    /// <summary>What customers can book, grouped into sections, with GRID's package prices. Only launched services.</summary>
    private static async Task<EnvelopeResult<CatalogueResponse>> Catalogue(
        GridDbContext db, IOptions<MarketplaceOptions> options, CancellationToken ct)
    {
        var categories = await db.ServiceCategories.Where(c => c.IsActive && c.IsLaunched)
            .OrderBy(c => c.SortOrder).ToListAsync(ct);
        var packages = await db.ServicePackages.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync(ct);

        var services = categories.Where(c => c.ParentCategoryId is null)
            .Select(c =>
            {
                var own = packages.Where(p => p.ServiceCategoryId == c.Id).ToList();
                return (Section: c.Section ?? "Services", Service: new CatalogueServiceResponse(
                    c.Id, c.Slug, c.Name, c.Description, c.BookingMode, own.Count == 0 ? 0 : own.Min(p => p.Price),
                    categories.Where(k => k.ParentCategoryId == c.Id).Select(k => k.Name).ToList(),
                    own.Select(PackageResponse.From).ToList()));
            })
            .Where(x => x.Service.Packages.Count > 0)
            .ToList();

        var sections = services.GroupBy(x => x.Section)
            .Select(g => new CatalogueSectionResponse(g.Key, g.Select(x => x.Service).ToList()))
            .ToList();
        return Envelope.Ok(new CatalogueResponse(sections, options.Value.FeePercent));
    }

    private static async Task<EnvelopeResult<List<ServiceAreaResponse>>> Areas(GridDbContext db, CancellationToken ct) =>
        Envelope.Ok(await db.ServiceAreas
            .Where(a => a.IsActive)
            .OrderBy(a => a.SortOrder)
            .Select(a => new ServiceAreaResponse(a.Id, a.Name, a.Region, a.Latitude, a.Longitude, a.RadiusKm))
            .ToListAsync(ct));

    /// <summary>Which area a device location is in, so the apps can show "we serve you here" before asking for more.</summary>
    private static async Task<EnvelopeResult<LocatedAreaResponse>> Locate(
        double? latitude, double? longitude, GridDbContext db, CancellationToken ct)
    {
        if (latitude is not { } lat || longitude is not { } lng || !Geo.IsInGhana(lat, lng))
            throw Errors.Validation([new FieldError("latitude", "Send a latitude and longitude in Ghana.")]);

        var areas = await db.ServiceAreas.Where(a => a.IsActive).ToListAsync(ct);
        var match = ServiceAreas.Locate(areas, new GeoPoint(lat, lng));
        return Envelope.Ok(match is null
            ? new LocatedAreaResponse(false, null, null)
            : new LocatedAreaResponse(true, ServiceAreaResponse.From(match.Value.Area), Math.Round(match.Value.DistanceKm, 1)));
    }

    /// <summary>An active top-level category; offerings are made against these only.</summary>
    internal static async Task<ServiceCategory> FindPrimaryAsync(GridDbContext db, Guid? id, CancellationToken ct) =>
        await db.ServiceCategories.SingleOrDefaultAsync(
            c => c.Id == id && c.ParentCategoryId == null && c.IsActive, ct)
        ?? throw Errors.NotFound("catalogue.category_not_found", "That service was not found.");
}

public static class ServiceAreas
{
    /// <summary>A point just outside an area's circle still belongs to it; areas are drawn tight.</summary>
    public const double EdgeToleranceKm = 2;

    /// <summary>
    /// The area a point belongs to: the nearest centre whose circle (plus a small tolerance)
    /// contains it. Areas overlap, so the nearest centre wins. Null when nothing is close enough.
    /// </summary>
    public static (ServiceArea Area, double DistanceKm)? Locate(IEnumerable<ServiceArea> areas, GeoPoint point)
    {
        (ServiceArea Area, double DistanceKm)? best = null;
        foreach (var area in areas)
        {
            var distance = Geo.DistanceKm(area.Centre, point);
            if (distance > area.RadiusKm + EdgeToleranceKm) continue;
            if (best is null || distance < best.Value.DistanceKm) best = (area, distance);
        }
        return best;
    }

    /// <summary>The area with this name, ignoring case and surrounding space. Names repeat across regions rarely; the first wins.</summary>
    public static ServiceArea? ByName(IEnumerable<ServiceArea> areas, string? name)
    {
        var wanted = name?.Trim();
        return string.IsNullOrEmpty(wanted)
            ? null
            : areas.FirstOrDefault(a => string.Equals(a.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }
}
