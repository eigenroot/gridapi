using Grid.Api.Common;
using Grid.Api.Features.UtilityAccounts;

namespace Grid.Api.Tests;

public class PhoneNumbersTests
{
    [Theory]
    [InlineData("0558151870")]
    [InlineData("558151870")]
    [InlineData("233558151870")]
    [InlineData("+233558151870")]
    [InlineData("+233 55 815 1870")]
    [InlineData("055-815-1870")]
    [InlineData("00233558151870")]
    public void Normalizes_every_accepted_form_to_233(string input)
    {
        Assert.True(PhoneNumbers.TryNormalize(input, out var normalized));
        Assert.Equal("233558151870", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("0302123456")]      // Accra landline, not mobile
    [InlineData("2345581518701")]   // too long
    [InlineData("234558151870")]    // Nigeria
    [InlineData("0458151870")]      // no 04x mobile prefix
    public void Rejects_numbers_that_are_not_Ghana_mobiles(string? input)
    {
        Assert.False(PhoneNumbers.TryNormalize(input, out _));
        Assert.Throws<ApiException>(() => PhoneNumbers.Normalize(input));
    }

    [Theory]
    [InlineData("233558151870", PhoneNetwork.Mtn)]
    [InlineData("233244000000", PhoneNetwork.Mtn)]
    [InlineData("233201234567", PhoneNetwork.Telecel)]
    [InlineData("233501234567", PhoneNetwork.Telecel)]
    [InlineData("233261234567", PhoneNetwork.AirtelTigo)]
    [InlineData("233571234567", PhoneNetwork.AirtelTigo)]
    [InlineData("233281234567", PhoneNetwork.Unknown)]
    public void Guesses_network_from_prefix(string phone, PhoneNetwork expected) =>
        Assert.Equal(expected, PhoneNumbers.GuessNetwork(phone));

    [Fact]
    public void Masks_for_logs() => Assert.Equal("23355****870", PhoneNumbers.Mask("233558151870"));

    [Theory]
    [InlineData(UtilityType.Ecg, "0123 4567 89", "0123456789")]
    [InlineData(UtilityType.Water, "40-123-456", "40123456")]
    [InlineData(UtilityType.Airtime, "0558151870", "233558151870")]
    [InlineData(UtilityType.DataBundle, "+233 20 123 4567", "233201234567")]
    public void Normalizes_utility_account_numbers(UtilityType type, string input, string expected)
    {
        Assert.Equal(expected, UtilityTypeRules.Normalize(type, input, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(UtilityType.Ecg, "123456789")]      // 9 < 10 digits
    [InlineData(UtilityType.Dstv, "123456789012")]  // 12 > 11 digits
    [InlineData(UtilityType.Water, "ABC12345")]
    [InlineData(UtilityType.Airtime, "0302123456")]
    public void Rejects_invalid_utility_account_numbers(UtilityType type, string input)
    {
        Assert.Null(UtilityTypeRules.Normalize(type, input, out var error));
        Assert.NotNull(error);
    }
}
