using Grid.Api.Common;

namespace Grid.Api.Features.UtilityAccounts;

public enum UtilityType { Ecg, Water, Dstv, Gotv, Startimes, Airtime, DataBundle, Internet }

/// <summary>
/// Account-number rules per utility. These mirror <c>GRID/lib/core/models/utility_type.dart</c>;
/// the server is the authority, so a change there must be made here too.
/// </summary>
public static class UtilityTypeRules
{
    private sealed record Rule(int MinDigits, int MaxDigits, bool IsPhone, string Label);

    private static readonly Dictionary<UtilityType, Rule> Rules = new()
    {
        [UtilityType.Ecg] = new(10, 13, false, "Meter number"),
        [UtilityType.Water] = new(8, 12, false, "Account number"),
        [UtilityType.Dstv] = new(10, 11, false, "Smartcard number"),
        [UtilityType.Gotv] = new(10, 11, false, "IUC number"),
        [UtilityType.Startimes] = new(10, 12, false, "Smartcard number"),
        [UtilityType.Airtime] = new(9, 12, true, "Phone number"),
        [UtilityType.DataBundle] = new(9, 12, true, "Phone number"),
        [UtilityType.Internet] = new(6, 12, false, "Account ID"),
    };

    /// <summary>
    /// Normalises an account number for <paramref name="type"/>: phone numbers become
    /// <c>233XXXXXXXXX</c>, everything else is digits only. Returns null and an error message
    /// when it isn't valid.
    /// </summary>
    public static string? Normalize(UtilityType type, string? input, out string? error)
    {
        var rule = Rules[type];
        error = null;

        if (rule.IsPhone)
        {
            if (PhoneNumbers.TryNormalize(input, out var phone)) return phone;
            error = "Enter a valid Ghana mobile number.";
            return null;
        }

        var raw = (input ?? "").Trim();
        var compact = new string(raw.Where(c => c is not (' ' or '-')).ToArray());
        if (compact.Length == 0 || !compact.All(char.IsAsciiDigit))
        {
            error = $"{rule.Label} must contain digits only.";
            return null;
        }
        if (compact.Length < rule.MinDigits || compact.Length > rule.MaxDigits)
        {
            error = rule.MinDigits == rule.MaxDigits
                ? $"{rule.Label} must be {rule.MinDigits} digits."
                : $"{rule.Label} must be {rule.MinDigits}–{rule.MaxDigits} digits.";
            return null;
        }
        return compact;
    }
}
