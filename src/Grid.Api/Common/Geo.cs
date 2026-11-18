namespace Grid.Api.Common;

/// <summary>A point on the map, in decimal degrees.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    /// <summary>Both coordinates or neither; null when either is missing.</summary>
    public static GeoPoint? From(double? latitude, double? longitude) =>
        latitude is { } lat && longitude is { } lng ? new GeoPoint(lat, lng) : null;
}

public static class Geo
{
    private const double EarthRadiusKm = 6371.0088;

    /// <summary>Great-circle distance. Straight-line, so road distance is always a little more.</summary>
    public static double DistanceKm(GeoPoint a, GeoPoint b)
    {
        var dLat = Radians(b.Latitude - a.Latitude);
        var dLng = Radians(b.Longitude - a.Longitude);
        var h = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(Radians(a.Latitude)) * Math.Cos(Radians(b.Latitude)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>A loose box around Ghana, to catch swapped or zeroed coordinates from a device.</summary>
    public static bool IsInGhana(double latitude, double longitude) =>
        latitude is >= 4.3 and <= 11.5 && longitude is >= -3.6 and <= 1.5;

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}
