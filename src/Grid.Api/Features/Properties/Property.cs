using Grid.Api.Data;

namespace Grid.Api.Features.Properties;

public enum PropertyKind { House, Apartment, Office }

/// <summary>
/// A saved home, apartment or office. Creating one does not prove ownership of the real estate.
/// Whether it is the person's default lives on <c>Consumer.DefaultPropertyId</c>.
/// </summary>
public sealed class Property : Entity
{
    public Guid OwnerConsumerId { get; set; }
    public required string Nickname { get; set; }
    public PropertyKind Kind { get; set; }
    public required string Area { get; set; }
    public required string City { get; set; }
    public required string Address { get; set; }
    public string? GhanaPostGps { get; set; }

    /// <summary>Where the home is, when the device shared it. Jobs are matched on this first.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>The service area the home falls in, worked out from the location or the area name. Null when GRID doesn't serve it yet.</summary>
    public Guid? ServiceAreaId { get; set; }

    /// <summary>Deleting archives, so receipts and bookings that refer to it stay intact.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }
}
