namespace Grid.Api.Common;

public enum PhoneNetwork { Unknown, Mtn, Telecel, AirtelTigo }

/// <summary>
/// Ghana mobile numbers. Everything is stored and sent to providers as <c>233XXXXXXXXX</c>
/// (12 digits, no plus), which both Hubtel and Arkesel accept.
/// </summary>
public static class PhoneNumbers
{
    // Two-digit network codes that follow the leading 0 (e.g. 055 → "55").
    private static readonly Dictionary<string, PhoneNetwork> Prefixes = new()
    {
        ["24"] = PhoneNetwork.Mtn, ["25"] = PhoneNetwork.Mtn, ["53"] = PhoneNetwork.Mtn,
        ["54"] = PhoneNetwork.Mtn, ["55"] = PhoneNetwork.Mtn, ["59"] = PhoneNetwork.Mtn,
        ["20"] = PhoneNetwork.Telecel, ["50"] = PhoneNetwork.Telecel,
        ["26"] = PhoneNetwork.AirtelTigo, ["27"] = PhoneNetwork.AirtelTigo,
        ["56"] = PhoneNetwork.AirtelTigo, ["57"] = PhoneNetwork.AirtelTigo,
    };

    /// <summary>
    /// Accepts <c>0558151870</c>, <c>558151870</c>, <c>233558151870</c>, <c>+233 55 815 1870</c>
    /// and <c>00233558151870</c>, and produces <c>233558151870</c>.
    /// </summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input)) return false;

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("00233")) digits = digits[2..];

        string national;
        if (digits.Length == 12 && digits.StartsWith("233")) national = digits[3..];
        else if (digits.Length == 10 && digits[0] == '0') national = digits[1..];
        else if (digits.Length == 9) national = digits;
        else return false;

        // Ghana mobile numbers start 2x or 5x after the trunk prefix.
        if (national[0] is not ('2' or '5')) return false;

        normalized = "233" + national;
        return true;
    }

    public static string Normalize(string? input, string field = "phoneNumber") =>
        TryNormalize(input, out var normalized) ? normalized : throw Errors.InvalidPhone(field);

    /// <summary>
    /// The network the number was originally issued on. Numbers can be ported, so this is a
    /// hint for display and routing, never a fact.
    /// </summary>
    public static PhoneNetwork GuessNetwork(string normalized) =>
        normalized.Length == 12 && Prefixes.TryGetValue(normalized.Substring(3, 2), out var network)
            ? network
            : PhoneNetwork.Unknown;

    /// <summary>For logs: <c>233558151870</c> → <c>23355****870</c>.</summary>
    public static string Mask(string normalized) =>
        normalized.Length < 8 ? "****" : normalized[..5] + "****" + normalized[^3..];
}
