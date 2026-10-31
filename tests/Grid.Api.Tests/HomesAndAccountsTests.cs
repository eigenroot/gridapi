using System.Net.Http.Json;
using System.Text.Json;
using Grid.Api.Tests.Support;

namespace Grid.Api.Tests;

public class HomesAndAccountsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static object Home(string nickname) => new
    {
        nickname, kind = "Apartment", area = "Osu", city = "Accra", address = "5 Oxford Street", ghanaPostGps = "ga-123-4567",
    };

    private static async Task<JsonElement> CreateHomeAsync(HttpClient client, string nickname) =>
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/properties", Home(nickname)));

    [Fact]
    public async Task First_home_becomes_default_and_archiving_moves_the_default()
    {
        var me = await api.SignInAsync("0201000001");
        var first = await CreateHomeAsync(me.Client, "Home");
        Assert.True(first.GetProperty("isPrimary").GetBoolean());
        Assert.Equal("GA-123-4567", first.GetProperty("ghanaPostGps").GetString());

        var second = await CreateHomeAsync(me.Client, "Office");
        Assert.False(second.GetProperty("isPrimary").GetBoolean());

        await Json.ExpectSuccessAsync(await me.Client.DeleteAsync($"/api/v1/properties/{first.GetProperty("id")}"));

        var list = await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/properties"));
        var remaining = Assert.Single(list.EnumerateArray());
        Assert.Equal("Office", remaining.GetProperty("nickname").GetString());
        Assert.True(remaining.GetProperty("isPrimary").GetBoolean());
    }

    [Fact]
    public async Task Default_home_can_be_chosen()
    {
        var me = await api.SignInAsync("0201000002");
        await CreateHomeAsync(me.Client, "Home");
        var office = await CreateHomeAsync(me.Client, "Office");

        var profile = await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync(
            "/api/v1/consumers/me/default-property", new { propertyId = office.GetProperty("id").GetGuid() }));
        Assert.Equal(office.GetProperty("id").GetGuid(), profile.GetProperty("defaultPropertyId").GetGuid());
    }

    [Fact]
    public async Task People_cannot_see_or_use_each_others_homes_and_accounts()
    {
        var owner = await api.SignInAsync("0201000003");
        var other = await api.SignInAsync("0201000004");
        var home = await CreateHomeAsync(owner.Client, "Home");
        var homeId = home.GetProperty("id").GetGuid();
        var account = await Json.ExpectSuccessAsync(await owner.Client.PostAsJsonAsync("/api/v1/utility-accounts",
            new { type = "Ecg", accountNumber = "0123456789", nickname = "Meter" }));

        await Json.ExpectErrorAsync(await other.Client.GetAsync($"/api/v1/properties/{homeId}"), 404, "properties.not_found");
        await Json.ExpectErrorAsync(await other.Client.PutAsJsonAsync($"/api/v1/properties/{homeId}", Home("Mine now")), 404, "properties.not_found");
        await Json.ExpectErrorAsync(await other.Client.DeleteAsync($"/api/v1/properties/{homeId}"), 404, "properties.not_found");
        await Json.ExpectErrorAsync(await other.Client.PutAsJsonAsync("/api/v1/consumers/me/default-property", new { propertyId = homeId }),
            404, "properties.not_found");
        await Json.ExpectErrorAsync(await other.Client.PostAsJsonAsync("/api/v1/utility-accounts",
            new { type = "Water", accountNumber = "40123456", nickname = "Water", propertyId = homeId }), 404, "properties.not_found");
        await Json.ExpectErrorAsync(await other.Client.GetAsync($"/api/v1/utility-accounts/{account.GetProperty("id")}"),
            404, "utility_accounts.not_found");

        Assert.Empty((await Json.ExpectSuccessAsync(await other.Client.GetAsync("/api/v1/properties"))).EnumerateArray());
        Assert.Empty((await Json.ExpectSuccessAsync(await other.Client.GetAsync("/api/v1/utility-accounts"))).EnumerateArray());
    }

    [Fact]
    public async Task Accounts_are_normalised_deduplicated_and_have_one_default_per_type()
    {
        var me = await api.SignInAsync("0201000005");

        var meter = await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/utility-accounts",
            new { type = "Ecg", accountNumber = "0123 4567 89", nickname = "Home meter" }));
        Assert.Equal("0123456789", meter.GetProperty("accountNumber").GetString());
        Assert.True(meter.GetProperty("isDefault").GetBoolean()); // first of its type

        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/utility-accounts",
            new { type = "Ecg", accountNumber = "0123456789", nickname = "Again" }), 409, "utility_accounts.duplicate");

        var shop = await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/utility-accounts",
            new { type = "Ecg", accountNumber = "9876543210", nickname = "Shop meter", isDefault = true }));
        Assert.True(shop.GetProperty("isDefault").GetBoolean());

        var phone = await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/utility-accounts",
            new { type = "Airtime", accountNumber = "055 123 4567", nickname = "Mum" }));
        Assert.Equal("233551234567", phone.GetProperty("accountNumber").GetString());

        var ecg = await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/utility-accounts?type=Ecg"));
        var defaults = ecg.EnumerateArray().Where(a => a.GetProperty("isDefault").GetBoolean()).ToList();
        Assert.Equal("Shop meter", Assert.Single(defaults).GetProperty("nickname").GetString());

        // Archiving the default hands it to the newest remaining account of that type.
        await Json.ExpectSuccessAsync(await me.Client.DeleteAsync($"/api/v1/utility-accounts/{shop.GetProperty("id")}"));
        ecg = await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/utility-accounts?type=Ecg"));
        var left = Assert.Single(ecg.EnumerateArray());
        Assert.True(left.GetProperty("isDefault").GetBoolean());

        // An archived number can be saved again.
        await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/utility-accounts",
            new { type = "Ecg", accountNumber = "9876543210", nickname = "Shop meter" }));
    }

    [Fact]
    public async Task Invalid_account_numbers_are_rejected()
    {
        var me = await api.SignInAsync("0201000006");
        var error = await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/utility-accounts",
            new { type = "Ecg", accountNumber = "12345", nickname = "Meter" }), 400, "utility_accounts.invalid_number");
        Assert.Equal("accountNumber", error.GetProperty("fields")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Archiving_a_home_unlinks_its_accounts()
    {
        var me = await api.SignInAsync("0201000007");
        var home = await CreateHomeAsync(me.Client, "Home");
        var account = await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/utility-accounts",
            new { type = "Water", accountNumber = "40123456", nickname = "Water", propertyId = home.GetProperty("id").GetGuid() }));

        await Json.ExpectSuccessAsync(await me.Client.DeleteAsync($"/api/v1/properties/{home.GetProperty("id")}"));

        var reloaded = await Json.ExpectSuccessAsync(await me.Client.GetAsync($"/api/v1/utility-accounts/{account.GetProperty("id")}"));
        Assert.Equal(JsonValueKind.Null, reloaded.GetProperty("propertyId").ValueKind);
    }

    [Fact]
    public async Task Home_validation_reports_each_field()
    {
        var me = await api.SignInAsync("0201000008");
        var error = await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/properties",
            new { nickname = "", area = "Osu", city = "", address = "x", ghanaPostGps = "nope" }), 400, "validation.failed");
        var fields = error.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("field").GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?> { "nickname", "city", "ghanaPostGps" }, fields);
    }
}
