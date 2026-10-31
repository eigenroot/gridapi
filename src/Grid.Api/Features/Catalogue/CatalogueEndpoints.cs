using Grid.Api.Common;
using Grid.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Catalogue;

public sealed record ServiceCategoryResponse(
    Guid Id,
    Guid? ParentCategoryId,
    string Slug,
    string Name,
    string? Description,
    PricingMode DefaultPricingMode,
    int SortOrder);

public sealed record ServiceAreaResponse(Guid Id, string Name, string Region);

/// <summary>Public reference data. GRID Pro loads it before sign-in, so neither read needs a token.</summary>
public static class CatalogueEndpoints
{
    public static void MapCatalogueEndpoints(this IEndpointRouteBuilder api)
    {
        var catalogue = api.MapGroup("").WithTags("Catalogue").AllowAnonymous();

        catalogue.MapGet("/service-categories", Categories);
        catalogue.MapGet("/service-areas", Areas);
    }

    private static async Task<EnvelopeResult<List<ServiceCategoryResponse>>> Categories(GridDbContext db, CancellationToken ct) =>
        Envelope.Ok(await db.ServiceCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.ParentCategoryId != null).ThenBy(c => c.SortOrder)
            .Select(c => new ServiceCategoryResponse(
                c.Id, c.ParentCategoryId, c.Slug, c.Name, c.Description, c.DefaultPricingMode, c.SortOrder))
            .ToListAsync(ct));

    private static async Task<EnvelopeResult<List<ServiceAreaResponse>>> Areas(GridDbContext db, CancellationToken ct) =>
        Envelope.Ok(await db.ServiceAreas
            .Where(a => a.IsActive)
            .OrderBy(a => a.SortOrder)
            .Select(a => new ServiceAreaResponse(a.Id, a.Name, a.Region))
            .ToListAsync(ct));

    /// <summary>An active top-level category; offerings are made against these only.</summary>
    internal static async Task<ServiceCategory> FindPrimaryAsync(GridDbContext db, Guid? id, CancellationToken ct) =>
        await db.ServiceCategories.SingleOrDefaultAsync(
            c => c.Id == id && c.ParentCategoryId == null && c.IsActive, ct)
        ?? throw Errors.NotFound("catalogue.category_not_found", "That service was not found.");
}
