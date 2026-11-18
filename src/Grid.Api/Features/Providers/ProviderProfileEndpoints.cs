using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Catalogue;
using Grid.Api.Features.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Providers;

public sealed record OperatingStatusRequest(OperatingStatus? OperatingStatus);

/// <summary>Either part may be sent alone: the device's location, or how far the provider will travel.</summary>
public sealed record ProviderLocationRequest(double? Latitude, double? Longitude, decimal? TravelRadiusKm);

/// <summary>What a provider may still change about how customers see them once they are approved.</summary>
public sealed record PublicProfileRequest(string? Description);

public sealed record SummaryOffering(
    Guid Id, string ServiceCategory, string Name, PricingMode PricingMode, decimal? Price, string? UnitName,
    bool SupportsSameDay, bool IsActive);

public sealed record SummaryPayoutMethod(
    PayoutMethodType MethodType, string ProviderName, string AccountName, string MaskedAccountNumber,
    PayoutVerificationStatus VerificationStatus);

/// <summary>
/// Everything the app's home and account screens show about the provider, in one read.
/// <c>rating</c> is the average of customers' ratings, null until the first. <c>missingToGoOnline</c> is empty when
/// <c>canGoOnline</c> is true.
/// </summary>
public sealed record ProviderSummaryResponse(
    Guid Id,
    ProviderAccountType AccountType,
    string DisplayName,
    string OwnerName,
    string PhoneNumber,
    string? Description,
    ProviderVerificationStatus VerificationStatus,
    OperatingStatus OperatingStatus,
    double? Rating,
    int Reviews,
    int CompletedJobs,
    DateTimeOffset MemberSince,
    List<SummaryOffering> Offerings,
    List<string> ServiceAreas,
    decimal TravelRadiusKm,
    bool HasLocation,
    SummaryPayoutMethod? PayoutMethod,
    bool CanGoOnline,
    List<MissingRequirement> MissingToGoOnline);

/// <summary>The provider's working profile after onboarding: going online, location, and the summary the app opens on.</summary>
public static class ProviderProfileEndpoints
{
    public static void MapProviderProfileEndpoints(this IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("/provider-accounts/me").WithTags("Provider profile").RequireAuthorization();

        me.MapGet("/summary", Summary);
        me.MapPut("/operating-status", SetOperatingStatus);
        me.MapPut("/location", SetLocation);
        me.MapPut("/profile", UpdatePublicProfile);
    }

    private static async Task<EnvelopeResult<ProviderSummaryResponse>> Summary(
        CurrentProvider provider, GridDbContext db,
        Microsoft.Extensions.Options.IOptions<MarketplaceOptions> options, CancellationToken ct)
    {
        var (consumer, account, _) = await provider.LoadAsync(ct);
        return Envelope.Ok(await SummaryAsync(db, consumer, account, options.Value, ct));
    }

    /// <summary>
    /// Going online starts offers coming in, so it needs everything a job needs: an approved
    /// account, a priced service, an area, working hours and somewhere to be paid. Going offline
    /// always works.
    /// </summary>
    private static async Task<EnvelopeResult<ProviderSummaryResponse>> SetOperatingStatus(
        OperatingStatusRequest body, CurrentProvider provider, GridDbContext db,
        Microsoft.Extensions.Options.IOptions<MarketplaceOptions> options, CancellationToken ct)
    {
        if (body.OperatingStatus is not { } wanted)
            throw Errors.Validation([new FieldError("operatingStatus", "Choose Online or Offline.")]);

        var (consumer, account, _) = await provider.LoadAsync(ct);
        if (wanted == OperatingStatus.Online)
        {
            var missing = await MissingToGoOnlineAsync(db, account, ct);
            if (missing.Count > 0)
            {
                throw new ApiException(409, "providers.not_job_ready", "You can't go online yet.",
                    new { missingRequirements = missing });
            }
        }

        account.OperatingStatus = wanted;
        await db.SaveChangesAsync(ct);
        return Envelope.Ok(await SummaryAsync(db, consumer, account, options.Value, ct));
    }

    private static async Task<EnvelopeResult<ProviderSummaryResponse>> SetLocation(
        ProviderLocationRequest body, CurrentProvider provider, GridDbContext db, TimeProvider time,
        Microsoft.Extensions.Options.IOptions<MarketplaceOptions> options, CancellationToken ct)
    {
        var v = new Validator();
        if (body.Latitude is null != body.Longitude is null)
            v.Add("latitude", "Send both latitude and longitude, or neither.");
        else if (body.Latitude is { } lat && body.Longitude is { } lng && !Geo.IsInGhana(lat, lng))
            v.Add("latitude", "That location isn't in Ghana.");
        if (body.TravelRadiusKm is <= 0 or > 100) v.Add("travelRadiusKm", "Enter a distance up to 100 km.");
        if (body.Latitude is null && body.TravelRadiusKm is null) v.Add("latitude", "Send a location or a travel distance.");
        v.ThrowIfInvalid();

        var (consumer, account, _) = await provider.LoadAsync(ct);
        if (body.Latitude is not null)
        {
            account.BaseLatitude = body.Latitude;
            account.BaseLongitude = body.Longitude;
            account.LocationUpdatedAt = time.GetUtcNow();
        }
        if (body.TravelRadiusKm is not null) account.TravelRadiusKm = body.TravelRadiusKm;
        await db.SaveChangesAsync(ct);
        return Envelope.Ok(await SummaryAsync(db, consumer, account, options.Value, ct));
    }

    /// <summary>The description is the provider's own words to customers, so it stays editable after approval.</summary>
    private static async Task<EnvelopeResult<ProviderSummaryResponse>> UpdatePublicProfile(
        PublicProfileRequest body, CurrentProvider provider, GridDbContext db,
        Microsoft.Extensions.Options.IOptions<MarketplaceOptions> options, CancellationToken ct)
    {
        var v = new Validator();
        var description = v.Optional("description", body.Description, 4000, "Description");
        v.ThrowIfInvalid();

        var (consumer, account, _) = await provider.LoadAsync(ct);
        account.Description = description;
        await db.SaveChangesAsync(ct);
        return Envelope.Ok(await SummaryAsync(db, consumer, account, options.Value, ct));
    }

    internal static async Task<List<MissingRequirement>> MissingToGoOnlineAsync(
        GridDbContext db, ProviderAccount account, CancellationToken ct)
    {
        var id = account.Id;
        var offerings = await db.ProviderOfferings.Where(o => o.ProviderAccountId == id && o.IsActive).ToListAsync(ct);
        (bool Met, string Code, string Message)[] checks =
        [
            (account.VerificationStatus == ProviderVerificationStatus.Approved,
                "verification.required", "GRID hasn't approved your application yet."),
            (offerings.Count > 0,
                "offering.required", "Turn on at least one service."),
            (await db.ProviderServiceAreas.AnyAsync(a => a.ProviderAccountId == id, ct),
                "service_area.required", "Choose at least one area you work in."),
            (await db.ProviderAvailabilityRules.AnyAsync(r => r.ProviderAccountId == id && r.IsAvailable, ct),
                "availability.required", "Choose the days you're available."),
            (await db.PayoutMethods.AnyAsync(m => m.ProviderAccountId == id && m.IsDefault && m.ArchivedAt == null, ct),
                "payout.required", "Add where you'd like to be paid."),
        ];
        return checks.Where(c => !c.Met).Select(c => new MissingRequirement(c.Code, c.Message)).ToList();
    }

    private static async Task<ProviderSummaryResponse> SummaryAsync(
        GridDbContext db, Consumers.Consumer consumer, ProviderAccount account, MarketplaceOptions options, CancellationToken ct)
    {
        var id = account.Id;
        var offerings = await (
            from o in db.ProviderOfferings
            join c in db.ServiceCategories on o.ServiceCategoryId equals c.Id
            where o.ProviderAccountId == id
            orderby c.SortOrder
            select new { o, c.Slug, c.Name }).ToListAsync(ct);
        var areas = await (
            from row in db.ProviderServiceAreas
            join a in db.ServiceAreas on row.ServiceAreaId equals a.Id
            where row.ProviderAccountId == id
            orderby a.SortOrder
            select new { a.Name, row.MaximumTravelDistanceKm }).ToListAsync(ct);
        var payout = await db.PayoutMethods.SingleOrDefaultAsync(m => m.ProviderAccountId == id && m.IsDefault && m.ArchivedAt == null, ct);
        var completed = await db.Jobs.CountAsync(j => j.ProviderAccountId == id && j.Stage == JobStage.Completed, ct);
        var missing = await MissingToGoOnlineAsync(db, account, ct);
        var (rating, reviews) = await ServiceRequestEndpoints.RatingAsync(db, id, ct);

        return new ProviderSummaryResponse(
            account.Id, account.AccountType, account.DisplayName, consumer.FullName, consumer.PhoneNumber,
            account.Description, account.VerificationStatus, account.OperatingStatus,
            rating, reviews, completed, account.CreatedAt,
            offerings.Select(x => new SummaryOffering(
                x.o.Id, x.Slug, x.Name, x.o.PricingMode,
                x.o.PricingMode switch
                {
                    PricingMode.FixedPrice => x.o.FixedPrice,
                    PricingMode.PerUnit => x.o.UnitPrice,
                    PricingMode.StartingFrom => x.o.StartingPrice,
                    _ => x.o.InspectionFee,
                },
                x.o.UnitName, x.o.SupportsSameDay, x.o.IsActive)).ToList(),
            areas.Select(a => a.Name).ToList(),
            account.TravelRadiusKm ?? areas.Max(a => a.MaximumTravelDistanceKm) ?? options.DefaultTravelRadiusKm,
            account.BaseLatitude is not null,
            payout is null ? null : new SummaryPayoutMethod(
                payout.MethodType, payout.ProviderName, payout.AccountName, payout.MaskedAccountNumber, payout.VerificationStatus),
            missing.Count == 0, missing);
    }
}
