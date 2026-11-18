using System.Text.RegularExpressions;
using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Catalogue;
using Grid.Api.Features.Consumers;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Properties;

public sealed record PropertyRequest(
    string? Nickname,
    PropertyKind? Kind,
    string? Area,
    string? City,
    string? Address,
    string? GhanaPostGps,
    double? Latitude = null,
    double? Longitude = null);

public sealed record PropertyResponse(
    Guid Id,
    string Nickname,
    PropertyKind Kind,
    string Area,
    string City,
    string Address,
    string? GhanaPostGps,
    double? Latitude,
    double? Longitude,
    Guid? ServiceAreaId,
    bool IsPrimary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static PropertyResponse From(Property p, Guid? defaultPropertyId) =>
        new(p.Id, p.Nickname, p.Kind, p.Area, p.City, p.Address, p.GhanaPostGps, p.Latitude, p.Longitude, p.ServiceAreaId,
            p.Id == defaultPropertyId, p.CreatedAt, p.UpdatedAt);
}

public static partial class PropertyEndpoints
{
    public static void MapPropertyEndpoints(this IEndpointRouteBuilder api)
    {
        var properties = api.MapGroup("/properties").WithTags("Homes").RequireAuthorization();

        properties.MapGet("", List);
        properties.MapPost("", Create);
        properties.MapGet("/{id:guid}", Get);
        properties.MapPut("/{id:guid}", Update);
        properties.MapDelete("/{id:guid}", Archive);
    }

    private static async Task<EnvelopeResult<List<PropertyResponse>>> List(
        CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var consumer = await current.LoadAsync(ct);
        var rows = await db.Properties
            .Where(p => p.OwnerConsumerId == consumer.Id && p.ArchivedAt == null)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);
        return Envelope.Ok(rows.Select(p => PropertyResponse.From(p, consumer.DefaultPropertyId)).ToList());
    }

    private static async Task<EnvelopeResult<PropertyResponse>> Get(
        Guid id, CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var consumer = await current.LoadAsync(ct);
        var property = await FindOwnedAsync(db, consumer.Id, id, ct);
        return Envelope.Ok(PropertyResponse.From(property, consumer.DefaultPropertyId));
    }

    /// <summary>Saves a home. The first one becomes the default automatically.</summary>
    private static async Task<EnvelopeResult<PropertyResponse>> Create(
        PropertyRequest body, CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var input = Validate(body);
        var consumer = await current.LoadAsync(ct);

        var property = new Property
        {
            OwnerConsumerId = consumer.Id,
            Nickname = input.Nickname,
            Kind = input.Kind,
            Area = input.Area,
            City = input.City,
            Address = input.Address,
            GhanaPostGps = input.GhanaPostGps,
            Latitude = input.Latitude,
            Longitude = input.Longitude,
        };
        property.ServiceAreaId = await ResolveAreaAsync(db, property, ct);
        db.Properties.Add(property);
        await db.SaveChangesAsync(ct);

        if (consumer.DefaultPropertyId is null)
        {
            consumer.DefaultPropertyId = property.Id;
            await db.SaveChangesAsync(ct);
        }

        return Envelope.Created(PropertyResponse.From(property, consumer.DefaultPropertyId));
    }

    /// <summary>Full replace: every field is sent every time.</summary>
    private static async Task<EnvelopeResult<PropertyResponse>> Update(
        Guid id, PropertyRequest body, CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var input = Validate(body);
        var consumer = await current.LoadAsync(ct);
        var property = await FindOwnedAsync(db, consumer.Id, id, ct);

        property.Nickname = input.Nickname;
        property.Kind = input.Kind;
        property.Area = input.Area;
        property.City = input.City;
        property.Address = input.Address;
        property.GhanaPostGps = input.GhanaPostGps;
        property.Latitude = input.Latitude;
        property.Longitude = input.Longitude;
        property.ServiceAreaId = await ResolveAreaAsync(db, property, ct);
        await db.SaveChangesAsync(ct);

        return Envelope.Ok(PropertyResponse.From(property, consumer.DefaultPropertyId));
    }

    /// <summary>
    /// Archives rather than deletes. Saved accounts linked to it are unlinked, and if it was the
    /// default, the most recently added remaining home takes over.
    /// </summary>
    private static async Task<EnvelopeResult<object>> Archive(
        Guid id, CurrentConsumer current, GridDbContext db, TimeProvider time, CancellationToken ct)
    {
        var consumer = await current.LoadAsync(ct);
        var property = await FindOwnedAsync(db, consumer.Id, id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        property.ArchivedAt = time.GetUtcNow();

        await db.UtilityAccounts
            .Where(a => a.OwnerConsumerId == consumer.Id && a.PropertyId == property.Id && a.ArchivedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PropertyId, (Guid?)null), ct);

        if (consumer.DefaultPropertyId == property.Id)
        {
            consumer.DefaultPropertyId = await db.Properties
                .Where(p => p.OwnerConsumerId == consumer.Id && p.ArchivedAt == null && p.Id != property.Id)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Envelope.Empty();
    }

    internal static async Task<Property> FindOwnedAsync(GridDbContext db, Guid consumerId, Guid id, CancellationToken ct) =>
        await db.Properties.SingleOrDefaultAsync(
            p => p.Id == id && p.OwnerConsumerId == consumerId && p.ArchivedAt == null, ct)
        ?? throw Errors.NotFound("properties.not_found", "That home was not found.");

    /// <summary>The location decides the area when there is one; otherwise the area name the person chose does.</summary>
    internal static async Task<Guid?> ResolveAreaAsync(GridDbContext db, Property property, CancellationToken ct)
    {
        var areas = await db.ServiceAreas.Where(a => a.IsActive).ToListAsync(ct);
        if (GeoPoint.From(property.Latitude, property.Longitude) is { } point)
            return ServiceAreas.Locate(areas, point)?.Area.Id;
        return ServiceAreas.ByName(areas, property.Area)?.Id;
    }

    private sealed record ValidInput(
        string Nickname, PropertyKind Kind, string Area, string City, string Address, string? GhanaPostGps,
        double? Latitude, double? Longitude);

    private static ValidInput Validate(PropertyRequest body)
    {
        var v = new Validator();
        var nickname = v.Required("nickname", body.Nickname, 60, "Name");
        var area = v.Required("area", body.Area, 100, "Area");
        var city = v.Required("city", body.City, 100, "City");
        var address = v.Required("address", body.Address, 200, "Address");
        var gps = v.Optional("ghanaPostGps", body.GhanaPostGps, 16, "GhanaPost GPS address")?.ToUpperInvariant();
        if (gps is not null && !GhanaPostGpsPattern().IsMatch(gps))
            v.Add("ghanaPostGps", "Enter a GhanaPost GPS address like GA-123-4567.");
        if (body.Latitude is null != body.Longitude is null)
            v.Add("latitude", "Send both latitude and longitude, or neither.");
        else if (body.Latitude is { } lat && body.Longitude is { } lng && !Geo.IsInGhana(lat, lng))
            v.Add("latitude", "That location isn't in Ghana.");
        v.ThrowIfInvalid();
        return new ValidInput(nickname, body.Kind ?? PropertyKind.House, area, city, address, gps, body.Latitude, body.Longitude);
    }

    [GeneratedRegex(@"^[A-Z]{2}-\d{3,4}-\d{3,4}$")]
    private static partial Regex GhanaPostGpsPattern();
}
