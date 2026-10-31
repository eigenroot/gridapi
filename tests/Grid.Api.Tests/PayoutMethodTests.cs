using System.Net.Http.Json;
using Grid.Api.Features.Providers;
using Grid.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Grid.Api.Tests;

public class PayoutMethodTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static object Momo(string number, bool makeDefault = true) => new
    {
        methodType = "MobileMoney", providerName = "MTN Mobile Money", accountName = "Akua Pay", accountNumber = number, makeDefault,
    };

    [Fact]
    public async Task Only_the_masked_number_is_returned_and_the_full_one_is_encrypted()
    {
        var me = await api.SignInAsync("0261000001", "pro", fullName: "Akua Pay");
        await Onboarding.CreateAccountAsync(me.Client, "Akua Pay");

        var response = await me.Client.PostAsJsonAsync("/api/v1/payout-methods", Momo("024 123 4567"));
        var method = await Json.ExpectSuccessAsync(response);
        Assert.Equal(201, (int)response.StatusCode);
        Assert.Equal("********4567", method.GetProperty("maskedAccountNumber").GetString());
        Assert.True(method.GetProperty("isDefault").GetBoolean());
        Assert.Equal("Unverified", method.GetProperty("verificationStatus").GetString());
        Assert.False(method.TryGetProperty("accountNumber", out _));

        var id = method.GetProperty("id").GetGuid();
        var stored = await api.WithDbAsync(db => db.PayoutMethods.SingleAsync(m => m.Id == id));
        Assert.DoesNotContain("1234567", System.Text.Encoding.UTF8.GetString(stored.AccountNumberEncrypted));
        var protector = api.Services.GetRequiredService<PayoutProtector>();
        Assert.Equal("233241234567", protector.Decrypt(stored.AccountNumberEncrypted));
    }

    [Fact]
    public async Task Resending_the_same_number_updates_instead_of_duplicating()
    {
        var me = await api.SignInAsync("0261000002", "pro", fullName: "Kwame Pay");
        await Onboarding.CreateAccountAsync(me.Client, "Kwame Pay");

        await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/payout-methods", Momo("0241234567")));
        var again = await me.Client.PostAsJsonAsync("/api/v1/payout-methods", Momo("+233 24 123 4567"));
        Assert.Equal(200, (int)again.StatusCode);
        Assert.Single((await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/payout-methods"))).EnumerateArray());

        // A second destination can take over as the default; there is only ever one.
        await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/payout-methods", new
        {
            methodType = "BankAccount", providerName = "GCB Bank", accountName = "Kwame Pay", accountNumber = "1234-5678-9012", makeDefault = true,
        }));
        var methods = await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/payout-methods"));
        Assert.Equal(2, methods.GetArrayLength());
        var defaults = methods.EnumerateArray().Where(m => m.GetProperty("isDefault").GetBoolean()).ToList();
        Assert.Equal("BankAccount", Assert.Single(defaults).GetProperty("methodType").GetString());
        Assert.Equal("********9012", defaults[0].GetProperty("maskedAccountNumber").GetString());
    }

    [Fact]
    public async Task Invalid_numbers_are_rejected()
    {
        var me = await api.SignInAsync("0261000003", "pro", fullName: "Bad Numbers");
        await Onboarding.CreateAccountAsync(me.Client, "Bad Numbers");

        var error = await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/payout-methods", Momo("12345")),
            400, "validation.failed");
        Assert.Equal("accountNumber", error.GetProperty("fields")[0].GetProperty("field").GetString());
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/payout-methods", new
        {
            methodType = "BankAccount", providerName = "GCB Bank", accountName = "Bad", accountNumber = "12#4",
        }), 400, "validation.failed");
    }
}
