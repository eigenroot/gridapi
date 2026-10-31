using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Catalogue;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Providers;

public sealed record OfferingRequest(
    Guid? ServiceCategoryId,
    string? CustomServiceName,
    string? Description,
    PricingMode? PricingMode,
    decimal? FixedPrice,
    decimal? StartingPrice,
    decimal? InspectionFee,
    decimal? UnitPrice,
    string? UnitName,
    bool? SupportsEmergency,
    bool? SupportsSameDay,
    bool? IsActive);

public sealed record OfferingResponse(
    Guid Id,
    Guid ServiceCategoryId,
    string? CustomServiceName,
    string? Description,
    PricingMode PricingMode,
    decimal? FixedPrice,
    decimal? StartingPrice,
    decimal? InspectionFee,
    decimal? UnitPrice,
    string? UnitName,
    bool SupportsEmergency,
    bool SupportsSameDay,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OfferingResponse From(ProviderOffering o) =>
        new(o.Id, o.ServiceCategoryId, o.CustomServiceName, o.Description, o.PricingMode, o.FixedPrice,
            o.StartingPrice, o.InspectionFee, o.UnitPrice, o.UnitName, o.SupportsEmergency, o.SupportsSameDay,
            o.IsActive, o.CreatedAt, o.UpdatedAt);
}

public sealed record ServiceAreaRequest(
    Guid? ServiceAreaId,
    decimal? MaximumTravelDistanceKm,
    bool? SupportsEmergency,
    bool? SupportsSameDay);

public sealed record ProviderServiceAreaResponse(
    Guid Id,
    Guid ServiceAreaId,
    string Name,
    string Region,
    decimal? MaximumTravelDistanceKm,
    bool SupportsEmergency,
    bool SupportsSameDay);

public sealed record AvailabilityRuleRequest(DayOfWeek? DayOfWeek, TimeOnly? StartTime, TimeOnly? EndTime, bool? IsAvailable);

public sealed record SetAvailabilityRequest(List<AvailabilityRuleRequest>? Rules);

public sealed record AvailabilityRuleResponse(Guid Id, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, bool IsAvailable)
{
    public static AvailabilityRuleResponse From(ProviderAvailabilityRule r) =>
        new(r.Id, r.DayOfWeek, r.StartTime, r.EndTime, r.IsAvailable);
}

/// <summary>What the provider offers, where, and when.</summary>
public static class ProviderSetupEndpoints
{
    private const decimal MaxPrice = 1_000_000m;
    private const string OneOfferingPerCategoryIndex = "ix_provider_offerings_provider_account_id_service_category_id";
    private const string OneRowPerAreaIndex = "ix_provider_service_areas_provider_account_id_service_area_id";

    public static void MapProviderSetupEndpoints(this IEndpointRouteBuilder api)
    {
        var offerings = api.MapGroup("/provider-offerings").WithTags("Provider onboarding").RequireAuthorization();
        offerings.MapGet("", ListOfferings);
        offerings.MapPost("", CreateOffering);
        offerings.MapPut("/{id:guid}", UpdateOffering);

        var areas = api.MapGroup("/provider-service-areas").WithTags("Provider onboarding").RequireAuthorization();
        areas.MapGet("", ListAreas);
        areas.MapPost("", AddArea);
        areas.MapDelete("/{id:guid}", RemoveArea);

        var availability = api.MapGroup("/provider-availability").WithTags("Provider onboarding").RequireAuthorization();
        availability.MapGet("/rules", GetAvailability);
        availability.MapPut("/rules", SetAvailability);
    }

    // ── Offerings ──────────────────────────────────────────────────────────

    private static async Task<EnvelopeResult<List<OfferingResponse>>> ListOfferings(
        CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var rows = await db.ProviderOfferings.Where(o => o.ProviderAccountId == accountId).OrderBy(o => o.CreatedAt).ToListAsync(ct);
        return Envelope.Ok(rows.Select(OfferingResponse.From).ToList());
    }

    /// <summary>
    /// Prices are optional here: the app saves the service before the provider has priced it.
    /// Readiness reports <c>pricing.required</c> until the chosen mode's price is set.
    /// </summary>
    private static async Task<EnvelopeResult<OfferingResponse>> CreateOffering(
        OfferingRequest body, CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadEditableAsync(ct)).Account.Id;
        var input = await ValidateOfferingAsync(db, body, ct);
        if (await db.ProviderOfferings.AnyAsync(o => o.ProviderAccountId == accountId && o.ServiceCategoryId == input.ServiceCategoryId, ct))
            throw OfferingExists();

        var offering = new ProviderOffering { ProviderAccountId = accountId };
        Apply(offering, input);
        db.ProviderOfferings.Add(offering);
        await SaveOfferingAsync(db, ct);
        return Envelope.Created(OfferingResponse.From(offering));
    }

    /// <summary>Full replace.</summary>
    private static async Task<EnvelopeResult<OfferingResponse>> UpdateOffering(
        Guid id, OfferingRequest body, CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadEditableAsync(ct)).Account.Id;
        var offering = await db.ProviderOfferings.SingleOrDefaultAsync(o => o.Id == id && o.ProviderAccountId == accountId, ct)
                       ?? throw Errors.NotFound("providers.offering_not_found", "That service was not found.");
        var input = await ValidateOfferingAsync(db, body, ct);
        if (await db.ProviderOfferings.AnyAsync(o => o.ProviderAccountId == accountId && o.ServiceCategoryId == input.ServiceCategoryId && o.Id != id, ct))
            throw OfferingExists();

        Apply(offering, input);
        await SaveOfferingAsync(db, ct);
        return Envelope.Ok(OfferingResponse.From(offering));
    }

    private static async Task<ProviderOffering> ValidateOfferingAsync(GridDbContext db, OfferingRequest body, CancellationToken ct)
    {
        var v = new Validator();
        if (body.ServiceCategoryId is null) v.Add("serviceCategoryId", "Choose a service.");
        var customName = v.Optional("customServiceName", body.CustomServiceName, 100, "Service name");
        var description = v.Optional("description", body.Description, 1000, "Description");
        var unitName = v.Optional("unitName", body.UnitName, 30, "Unit");
        Price(v, "fixedPrice", body.FixedPrice);
        Price(v, "startingPrice", body.StartingPrice);
        Price(v, "inspectionFee", body.InspectionFee);
        Price(v, "unitPrice", body.UnitPrice);
        v.ThrowIfInvalid();

        var category = await CatalogueEndpoints.FindPrimaryAsync(db, body.ServiceCategoryId, ct);
        return new ProviderOffering
        {
            ServiceCategoryId = category.Id,
            CustomServiceName = customName,
            Description = description,
            PricingMode = body.PricingMode ?? category.DefaultPricingMode,
            FixedPrice = body.FixedPrice,
            StartingPrice = body.StartingPrice,
            InspectionFee = body.InspectionFee,
            UnitPrice = body.UnitPrice,
            UnitName = unitName,
            SupportsEmergency = body.SupportsEmergency ?? false,
            SupportsSameDay = body.SupportsSameDay ?? false,
            IsActive = body.IsActive ?? true,
        };

        static void Price(Validator v, string field, decimal? value)
        {
            if (value is < 0 or > MaxPrice) v.Add(field, $"Enter an amount between 0 and {MaxPrice:N0}.");
        }
    }

    private static void Apply(ProviderOffering target, ProviderOffering input)
    {
        target.ServiceCategoryId = input.ServiceCategoryId;
        target.CustomServiceName = input.CustomServiceName;
        target.Description = input.Description;
        target.PricingMode = input.PricingMode;
        target.FixedPrice = input.FixedPrice;
        target.StartingPrice = input.StartingPrice;
        target.InspectionFee = input.InspectionFee;
        target.UnitPrice = input.UnitPrice;
        target.UnitName = input.UnitName;
        target.SupportsEmergency = input.SupportsEmergency;
        target.SupportsSameDay = input.SupportsSameDay;
        target.IsActive = input.IsActive;
    }

    private static async Task SaveOfferingAsync(GridDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(OneOfferingPerCategoryIndex))
        {
            throw OfferingExists();
        }
    }

    private static ApiException OfferingExists() =>
        Errors.Conflict("providers.offering_exists", "You already offer this service.");

    // ── Service areas ──────────────────────────────────────────────────────

    private static async Task<EnvelopeResult<List<ProviderServiceAreaResponse>>> ListAreas(
        CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        return Envelope.Ok(await AreasQuery(db, accountId).ToListAsync(ct));
    }

    /// <summary>Adding an area already on the list updates it rather than duplicating it.</summary>
    private static async Task<EnvelopeResult<ProviderServiceAreaResponse>> AddArea(
        ServiceAreaRequest body, CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var v = new Validator();
        if (body.ServiceAreaId is null) v.Add("serviceAreaId", "Choose an area.");
        if (body.MaximumTravelDistanceKm is < 0 or > 500) v.Add("maximumTravelDistanceKm", "Enter a distance up to 500 km.");
        v.ThrowIfInvalid();

        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        if (!await db.ServiceAreas.AnyAsync(a => a.Id == body.ServiceAreaId && a.IsActive, ct))
            throw Errors.NotFound("catalogue.area_not_found", "That area was not found.");

        var row = await db.ProviderServiceAreas.SingleOrDefaultAsync(
            a => a.ProviderAccountId == accountId && a.ServiceAreaId == body.ServiceAreaId, ct);
        var created = row is null;
        row ??= db.ProviderServiceAreas.Add(new ProviderServiceArea
        {
            ProviderAccountId = accountId, ServiceAreaId = body.ServiceAreaId!.Value,
        }).Entity;
        row.MaximumTravelDistanceKm = body.MaximumTravelDistanceKm;
        row.SupportsEmergency = body.SupportsEmergency ?? false;
        row.SupportsSameDay = body.SupportsSameDay ?? false;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(OneRowPerAreaIndex))
        {
            // The same area was added concurrently; the other request's row stands.
            created = false;
        }

        var response = await AreasQuery(db, accountId, body.ServiceAreaId).SingleAsync(ct);
        return created ? Envelope.Created(response) : Envelope.Ok(response);
    }

    private static async Task<EnvelopeResult<object>> RemoveArea(
        Guid id, CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var removed = await db.ProviderServiceAreas.Where(a => a.Id == id && a.ProviderAccountId == accountId).ExecuteDeleteAsync(ct);
        if (removed == 0) throw Errors.NotFound("providers.service_area_not_found", "That area is not on your list.");
        return Envelope.Empty();
    }

    private static IQueryable<ProviderServiceAreaResponse> AreasQuery(GridDbContext db, Guid accountId, Guid? serviceAreaId = null) =>
        from row in db.ProviderServiceAreas
        join area in db.ServiceAreas on row.ServiceAreaId equals area.Id
        where row.ProviderAccountId == accountId && (serviceAreaId == null || row.ServiceAreaId == serviceAreaId)
        orderby area.SortOrder
        select new ProviderServiceAreaResponse(row.Id, row.ServiceAreaId, area.Name, area.Region,
            row.MaximumTravelDistanceKm, row.SupportsEmergency, row.SupportsSameDay);

    // ── Availability ───────────────────────────────────────────────────────

    private static async Task<EnvelopeResult<List<AvailabilityRuleResponse>>> GetAvailability(
        CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        return Envelope.Ok(await RulesAsync(db, accountId, ct));
    }

    /// <summary>Full replace of the weekly pattern: at most one rule per day.</summary>
    private static async Task<EnvelopeResult<List<AvailabilityRuleResponse>>> SetAvailability(
        SetAvailabilityRequest body, CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var rules = body.Rules ?? [];
        var v = new Validator();
        var seen = new HashSet<DayOfWeek>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule.DayOfWeek is not { } day) v.Add($"rules[{i}].dayOfWeek", "Choose a day.");
            else if (!seen.Add(day)) v.Add($"rules[{i}].dayOfWeek", "Each day can appear once.");

            if (rule.IsAvailable != true) continue;
            if (rule.StartTime is null) v.Add($"rules[{i}].startTime", "Choose a start time.");
            if (rule.EndTime is null) v.Add($"rules[{i}].endTime", "Choose an end time.");
            if (rule.StartTime >= rule.EndTime) v.Add($"rules[{i}].endTime", "End time must be after the start time.");
        }
        v.ThrowIfInvalid();

        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.ProviderAvailabilityRules.Where(r => r.ProviderAccountId == accountId).ExecuteDeleteAsync(ct);
        db.ProviderAvailabilityRules.AddRange(rules.Select(r => new ProviderAvailabilityRule
        {
            ProviderAccountId = accountId,
            DayOfWeek = r.DayOfWeek!.Value,
            StartTime = r.StartTime ?? TimeOnly.MinValue,
            EndTime = r.EndTime ?? TimeOnly.MinValue,
            IsAvailable = r.IsAvailable == true,
        }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Envelope.Ok(await RulesAsync(db, accountId, ct));
    }

    /// <summary>Monday first, as the app lists them.</summary>
    private static async Task<List<AvailabilityRuleResponse>> RulesAsync(GridDbContext db, Guid accountId, CancellationToken ct)
    {
        var rules = await db.ProviderAvailabilityRules.Where(r => r.ProviderAccountId == accountId).ToListAsync(ct);
        return rules.OrderBy(r => ((int)r.DayOfWeek + 6) % 7).Select(AvailabilityRuleResponse.From).ToList();
    }
}
