using Grid.Api.Features.BillPayments;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Grid.Api.Tests;

public class BillPaymentOptionsTests
{
    private static BillPaymentOptions Valid() => new()
    {
        Enabled = true, CallbackBaseUrl = "https://grid.example",
        Hubtel = new() { ApiId = "id", ApiKey = "key", CollectionAccountNumber = "11684", DisbursementAccountNumber = "11691", CallbackSecret = new string('x', 32) },
    };

    private static bool Passes(BillPaymentOptions o) => new BillPaymentOptionsValidator(new Production()).Validate(null, o).Succeeded;

    [Fact]
    public void Switched_off_needs_no_configuration() => Assert.True(Passes(new BillPaymentOptions()));

    [Fact]
    public void A_complete_configuration_passes() => Assert.True(Passes(Valid()));

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("DisbursementAccountNumber")]
    [InlineData("CallbackSecret")]
    public void Hubtel_credentials_both_accounts_and_a_callback_secret_are_required(string missing)
    {
        var o = Valid();
        typeof(HubtelPaymentOptions).GetProperty(missing)!.SetValue(o.Hubtel, "");
        Assert.False(Passes(o));
    }

    [Fact]
    public void Plain_HTTP_unknown_products_and_bad_operator_numbers_are_rejected()
    {
        var o = Valid(); o.Hubtel.CommissionBaseUrl = "http://cs.hubtel.com";
        Assert.False(Passes(o));
        o = Valid(); o.DisabledProducts.Add("Netflix");
        Assert.False(Passes(o));
        o = Valid(); o.FinanceOperators.Add("12345");
        Assert.False(Passes(o));
    }

    [Fact]
    public void Catalogue_holds_Hubtels_13_products()
    {
        Assert.Equal(13, BillProducts.All.Count);
        Assert.Equal(BillProducts.All.Count, BillProducts.All.Select(p => p.Code).Distinct().Count());
        Assert.Equal("e6d6bac062b5499cb1ece1ac3d742a84", BillProducts.Require("EcgPostpaid").ServiceId);
        Assert.Equal(100m, BillProducts.Require("AtAirtime").MaxAmount);
        Assert.True(BillProducts.Require("MtnData").FixedPrice);
    }

    private sealed class Production : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
