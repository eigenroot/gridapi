using Grid.Api.Features.Consumers;
using Grid.Api.Features.Kyc;
using Grid.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Tests;

public class KycTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private Task<Consumer> LoadAsync(string phone) =>
        api.WithDbAsync(db => db.Consumers.AsNoTracking().SingleAsync(c => c.PhoneNumber == phone));

    [Fact]
    public async Task Found_name_is_stored_apart_from_the_self_provided_name()
    {
        api.Kyc.Respond = _ => new IdentityLookupResult(IdentityLookupOutcome.Found, "KWAME ASANTE MENSAH");
        await api.SignInAsync("0241000001", fullName: "Kwame Mensah");

        await api.RunKycAsync();

        var row = await LoadAsync("233241000001");
        Assert.Equal(KycStatus.Verified, row.KycStatus);
        Assert.Equal("KWAME ASANTE MENSAH", row.KycName);
        Assert.Equal("fake", row.KycProvider);
        Assert.NotNull(row.KycCheckedAt);
        Assert.Equal("Kwame", row.FirstName);
        Assert.Equal("Mensah", row.LastName);
    }

    [Fact]
    public async Task Not_registered_number_is_not_retried()
    {
        api.Kyc.Respond = _ => new IdentityLookupResult(IdentityLookupOutcome.NotFound, Error: "HTTP 404 3000");
        await api.SignInAsync("0241000002");

        await api.RunKycAsync();
        var calls = api.Kyc.Calls;
        api.Time.Advance(TimeSpan.FromDays(1));
        await api.RunKycAsync();

        var row = await LoadAsync("233241000002");
        Assert.Equal(KycStatus.NotFound, row.KycStatus);
        Assert.Null(row.KycName);
        Assert.Equal(calls, api.Kyc.Calls);
    }

    [Fact]
    public async Task Provider_trouble_retries_with_backoff_then_gives_up()
    {
        api.Kyc.Respond = _ => new IdentityLookupResult(IdentityLookupOutcome.Retry, Error: "HTTP 424");
        await api.SignInAsync("0241000003");

        await api.RunKycAsync();
        var row = await LoadAsync("233241000003");
        Assert.Equal(KycStatus.Pending, row.KycStatus);
        Assert.Equal(1, row.KycAttempts);
        Assert.Equal("HTTP 424", row.KycLastError);

        // Not due yet: nothing happens.
        await api.RunKycAsync();
        Assert.Equal(1, (await LoadAsync("233241000003")).KycAttempts);

        foreach (var wait in new[] { 1, 5, 30, 120 })
        {
            api.Time.Advance(TimeSpan.FromMinutes(wait) + TimeSpan.FromSeconds(1));
            await api.RunKycAsync();
        }

        row = await LoadAsync("233241000003");
        Assert.Equal(KycStatus.Failed, row.KycStatus);
        Assert.Equal(5, row.KycAttempts);
        Assert.Null(row.KycNextAttemptAt);
    }

    [Fact]
    public async Task Disabled_lookup_leaves_people_queued_and_backfills_when_enabled()
    {
        api.Kyc.Enabled = false;
        try
        {
            await api.SignInAsync("0241000004");
            Assert.Equal(0, await api.RunKycAsync());
            Assert.Equal(KycStatus.NotChecked, (await LoadAsync("233241000004")).KycStatus);
        }
        finally
        {
            api.Kyc.Enabled = true;
        }

        api.Kyc.Respond = _ => new IdentityLookupResult(IdentityLookupOutcome.Found, "AKOSUA BOATENG");
        await api.RunKycAsync();
        Assert.Equal("AKOSUA BOATENG", (await LoadAsync("233241000004")).KycName);
    }

    [Fact]
    public async Task Kyc_is_never_returned_to_apps()
    {
        var signedIn = await api.SignInAsync("0241000005");
        await api.RunKycAsync();
        var body = await (await signedIn.Client.GetAsync("/api/v1/consumers/me")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("kyc", body, StringComparison.OrdinalIgnoreCase);
    }
}
