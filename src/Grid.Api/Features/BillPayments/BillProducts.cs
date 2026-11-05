using Grid.Api.Common;
using Grid.Api.Features.UtilityAccounts;

namespace Grid.Api.Features.BillPayments;

/// <summary>How a product's beneficiary is looked up before an order is placed.</summary>
public enum ProductLookup
{
    /// <summary>No upstream lookup (airtime). The number itself is the beneficiary.</summary>
    None,
    /// <summary>GET <c>?destination={account}</c> → name / amountDue / account rows (TV, broadband).</summary>
    Account,
    /// <summary>GET <c>?destination={phone}</c> → the meters linked to that phone (ECG).</summary>
    PhoneMeters,
    /// <summary>GET <c>?destination={meter}&amp;mobile={phone}</c> → name, amountDue and a one-time sessionId (Ghana Water).</summary>
    WaterMeter,
    /// <summary>GET <c>?destination={phone}</c> → purchasable bundles (data).</summary>
    Bundles,
}

/// <summary>
/// One purchasable Hubtel Commission Services product. <see cref="ServiceId"/> values come from
/// Hubtel's Commission Services docs and can be overridden with
/// <c>Payments:Hubtel:ServiceIds:{Code}</c>.
/// </summary>
public sealed record BillProduct(
    string Code,
    UtilityType Type,
    string Name,
    string ServiceId,
    ProductLookup Lookup,
    PhoneNetwork? Network = null,
    decimal? MaxAmount = null)
{
    /// <summary>Whether the beneficiary is a mobile number (airtime, data) rather than an account.</summary>
    public bool PhoneDestination => Lookup is ProductLookup.None or ProductLookup.Bundles;

    /// <summary>Data bundles are priced by the telco; every other product takes a free amount.</summary>
    public bool FixedPrice => Lookup == ProductLookup.Bundles;

    /// <summary>What a successful purchase means for the customer.</summary>
    public string ResultKind => Type == UtilityType.Ecg ? "RemoteMeterCredit" : "AccountCredited";
}

public static class BillProducts
{
    public static readonly IReadOnlyList<BillProduct> All =
    [
        new("EcgPrepaid", UtilityType.Ecg, "ECG prepaid", "e6d6bac062b5499cb1ece1ac3d742a84", ProductLookup.PhoneMeters),
        new("EcgPostpaid", UtilityType.Ecg, "ECG postpaid", "e6d6bac062b5499cb1ece1ac3d742a84", ProductLookup.PhoneMeters),
        new("GhanaWater", UtilityType.Water, "Ghana Water", "6c1e8a82d2e84feeb8bfd6be2790d71d", ProductLookup.WaterMeter),
        new("Dstv", UtilityType.Dstv, "DStv", "297a96656b5846ad8b00d5d41b256ea7", ProductLookup.Account),
        new("Gotv", UtilityType.Gotv, "GOtv", "e6ceac7f3880435cb30b048e9617eb41", ProductLookup.Account),
        new("Startimes", UtilityType.Startimes, "StarTimes", "6598652d34ea4112949c93c079c501ce", ProductLookup.Account),
        new("MtnData", UtilityType.DataBundle, "MTN data", "b230733cd56b4a0fad820e39f66bc27c", ProductLookup.Bundles, PhoneNetwork.Mtn),
        new("TelecelData", UtilityType.DataBundle, "Telecel data", "fa27127ba039455da04a2ac8a1613e00", ProductLookup.Bundles, PhoneNetwork.Telecel),
        new("AtData", UtilityType.DataBundle, "AT data", "06abd92da459428496967612463575ca", ProductLookup.Bundles, PhoneNetwork.AirtelTigo),
        // Hubtel caps a single airtime top-up at GHS 100.
        new("MtnAirtime", UtilityType.Airtime, "MTN airtime", "fdd76c884e614b1c8f669a3207b09a98", ProductLookup.None, PhoneNetwork.Mtn, 100m),
        new("TelecelAirtime", UtilityType.Airtime, "Telecel airtime", "f4be83ad74c742e185224fdae1304800", ProductLookup.None, PhoneNetwork.Telecel, 100m),
        new("AtAirtime", UtilityType.Airtime, "AT airtime", "dae2142eb5a14c298eace60240c09e4b", ProductLookup.None, PhoneNetwork.AirtelTigo, 100m),
        new("TelecelBroadband", UtilityType.Internet, "Telecel Broadband", "b9a1aa246ba748f9ba01ca4cdbb3d1d3", ProductLookup.Account),
    ];

    private static readonly Dictionary<string, BillProduct> ByCode = All.ToDictionary(p => p.Code, StringComparer.Ordinal);

    public static BillProduct? Find(string? code) => code is not null && ByCode.TryGetValue(code, out var product) ? product : null;

    public static BillProduct Require(string? code) => Find(code)
        ?? throw Errors.Validation([new("productCode", "Choose a supported product.")]);
}
