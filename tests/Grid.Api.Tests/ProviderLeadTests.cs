using System.Net.Http.Json;
using System.Text.Json;
using Grid.Api.Features.ProviderLeads;
using Grid.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Tests;

/// <summary>Leads switched on, with a rate limit small enough to hit.</summary>
public sealed class LeadsApiFactory : ApiFactory
{
    public const string WebKey = "test-web-key-0123456789-0123456789-0123";
    public const string DownloadUrl = "https://grid.test/download/pro";
    public const int PerIpPerHour = 5;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Leads:WebKey", WebKey);
        builder.UseSetting("Leads:ProDownloadUrl", DownloadUrl);
        builder.UseSetting("Leads:PerIpPerHour", PerIpPerHour.ToString());
    }
}

public class ProviderLeadTests(LeadsApiFactory api) : IClassFixture<LeadsApiFactory>
{
    private static int _ip;

    /// <summary>Each test gets its own visitor IP, so the shared rate limiter never mixes them up.</summary>
    private static string NextIp() => $"203.0.113.{Interlocked.Increment(ref _ip)}";

    private static object Body(
        string phone, string fullName = "Akosua Boateng", string[]? services = null, string area = "East Legon",
        string experience = "1to3", string? source = "facebook", string? medium = "paid", string? campaign = "cleaners-nov",
        bool consent = true) =>
        new { fullName, phoneNumber = phone, services = services ?? ["cleaning"], area, experience, source, medium, campaign, consent };

    private Task<HttpResponseMessage> ApplyAsync(object body, string? ip = null, string? key = LeadsApiFactory.WebKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/provider-leads") { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add(ProviderLeadEndpoints.WebKeyHeader, key);
        request.Headers.Add(ProviderLeadEndpoints.ClientIpHeader, ip ?? NextIp());
        return api.CreateClient().SendAsync(request);
    }

    private int TextsTo(string normalized) =>
        api.Sms.Sent.Count(m => m.To == normalized && m.Purpose == ProviderLeadEndpoints.SmsPurpose);

    private async Task<JsonElement> LeadsAsync(string query = "")
    {
        var ops = await Market.OpsAsync(api);
        return await Json.ExpectSuccessAsync(await ops.GetAsync($"/api/v1/ops/provider-leads{query}"));
    }

    private async Task<JsonElement> LeadRowAsync(string normalized) =>
        (await LeadsAsync()).GetProperty("leads").EnumerateArray()
        .Single(l => l.GetProperty("phoneNumber").GetString() == normalized);

    [Fact]
    public async Task Applying_creates_a_lead_and_texts_the_download_link()
    {
        var data = await Json.ExpectSuccessAsync(await ApplyAsync(Body("024 700 0001", "  Akosua  Boateng ", ["cleaning", "Laundry", "cleaning"])));

        Assert.Matches(@"^L-\d{4,}$", data.GetProperty("reference").GetString());
        Assert.False(data.GetProperty("alreadyRegistered").GetBoolean());

        var lead = await api.WithDbAsync(db => db.ProviderLeads.SingleAsync(l => l.PhoneNumber == "233247000001"));
        Assert.Equal("Akosua  Boateng", lead.FullName);
        Assert.Equal(["cleaning", "laundry"], lead.Services);
        Assert.Equal("1to3", lead.Experience);
        Assert.Equal("facebook", lead.Source);
        Assert.NotNull(lead.SmsSentAt);
        Assert.Equal($"L-{lead.Number}", data.GetProperty("reference").GetString());

        var sms = Assert.Single(api.Sms.Sent, m => m.To == "233247000001");
        Assert.Equal(ProviderLeadEndpoints.SmsPurpose, sms.Purpose);
        Assert.Equal(
            $"GRID Pro: thanks for applying, Akosua. Get the app: {LeadsApiFactory.DownloadUrl}. " +
            "Sign in with this number to finish. GRID will never ask for your MoMo PIN.", sms.Body);
    }

    [Fact]
    public async Task A_missing_or_wrong_web_key_is_refused()
    {
        await Json.ExpectErrorAsync(await ApplyAsync(Body("0247000002"), key: null), 401, "leads.unauthorized");
        await Json.ExpectErrorAsync(await ApplyAsync(Body("0247000002"), key: "not-the-key"), 401, "leads.unauthorized");

        Assert.False(await api.WithDbAsync(db => db.ProviderLeads.AnyAsync(l => l.PhoneNumber == "233247000002")));
        Assert.Equal(0, TextsTo("233247000002"));
    }

    [Fact]
    public async Task Every_problem_comes_back_keyed_by_field()
    {
        var error = await Json.ExpectErrorAsync(await ApplyAsync(new
        {
            fullName = " ", phoneNumber = "12345", services = new[] { "plumbing" }, area = "", experience = "10",
            source = new string('x', 101), consent = false,
        }), 400, "validation.failed");

        var fields = error.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("field").GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?> { "fullName", "phoneNumber", "services", "area", "experience", "source", "consent" }, fields);

        error = await Json.ExpectErrorAsync(await ApplyAsync(new { fullName = "Kofi", phoneNumber = "0247000003", area = "Madina", experience = "lt1", consent = true }),
            400, "validation.failed");
        Assert.Equal("services", Assert.Single(error.GetProperty("fields").EnumerateArray()).GetProperty("field").GetString());
    }

    [Fact]
    public async Task Applying_again_updates_the_lead_keeps_the_first_source_and_texts_once_per_six_hours()
    {
        var first = await Json.ExpectSuccessAsync(await ApplyAsync(Body("0247000004", "Kofi Mensah", source: "flyer", medium: null, campaign: null)));
        var second = await Json.ExpectSuccessAsync(await ApplyAsync(
            Body("+233 24 700 0004", "Kofi A. Mensah", ["ac-servicing", "pest-control"], "Madina", "5plus", "whatsapp", "share", "x")));

        Assert.Equal(first.GetProperty("reference").GetString(), second.GetProperty("reference").GetString());
        var lead = await api.WithDbAsync(db => db.ProviderLeads.SingleAsync(l => l.PhoneNumber == "233247000004"));
        Assert.Equal("Kofi A. Mensah", lead.FullName);
        Assert.Equal(["ac-servicing", "pest-control"], lead.Services);
        Assert.Equal("Madina", lead.Area);
        Assert.Equal("5plus", lead.Experience);
        Assert.Equal("flyer", lead.Source);
        Assert.Null(lead.Medium);
        Assert.Equal(1, TextsTo("233247000004"));

        api.Time.Advance(TimeSpan.FromHours(6).Add(TimeSpan.FromMinutes(1)));
        await Json.ExpectSuccessAsync(await ApplyAsync(Body("0247000004", "Kofi Mensah")));
        Assert.Equal(2, TextsTo("233247000004"));
    }

    [Fact]
    public async Task A_failed_text_does_not_fail_the_application_and_the_next_one_retries()
    {
        api.Sms.Fail = true;
        try
        {
            await Json.ExpectSuccessAsync(await ApplyAsync(Body("0247000005")));
        }
        finally
        {
            api.Sms.Fail = false;
        }
        Assert.Null((await api.WithDbAsync(db => db.ProviderLeads.SingleAsync(l => l.PhoneNumber == "233247000005"))).SmsSentAt);

        await Json.ExpectSuccessAsync(await ApplyAsync(Body("0247000005")));
        Assert.Equal(1, TextsTo("233247000005"));
    }

    [Fact]
    public async Task Too_many_applications_from_one_visitor_are_rate_limited()
    {
        var ip = NextIp();
        for (var i = 0; i < LeadsApiFactory.PerIpPerHour; i++)
            await Json.ExpectSuccessAsync(await ApplyAsync(Body("0247000006"), ip));

        var error = await Json.ExpectErrorAsync(await ApplyAsync(Body("0247000006"), ip), 429, "leads.rate_limited");
        Assert.True(error.GetProperty("detail").GetProperty("retryAfterSeconds").GetInt32() > 0);

        // Another visitor behind the same website server is unaffected.
        await Json.ExpectSuccessAsync(await ApplyAsync(Body("0247000006")));
    }

    [Fact]
    public async Task Pro_sign_in_and_account_creation_link_the_lead()
    {
        await Json.ExpectSuccessAsync(await ApplyAsync(Body("0247000007", source: null, medium: null, campaign: null)));
        var row = await LeadRowAsync("233247000007");
        Assert.Equal("Applied", row.GetProperty("stage").GetString());
        Assert.Equal(ProviderLeadStages.Direct, row.GetProperty("source").GetString());

        // Signing in to GRID Home doesn't count.
        await api.SignInAsync("0247000007", "consumer");
        Assert.Equal("Applied", (await LeadRowAsync("233247000007")).GetProperty("stage").GetString());

        api.Time.Advance(TimeSpan.FromMinutes(2));
        var pro = await api.SignInAsync("0247000007", "pro", "Ama Owusu");
        row = await LeadRowAsync("233247000007");
        Assert.Equal("SignedIn", row.GetProperty("stage").GetString());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("signedInAt").ValueKind);

        var account = await Onboarding.CreateAccountAsync(pro.Client);
        await Json.ExpectSuccessAsync(await pro.Client.PutAsJsonAsync("/api/v1/provider-applications/me/current-step", new { step = 3 }));
        row = await LeadRowAsync("233247000007");
        Assert.Equal("Onboarding", row.GetProperty("stage").GetString());
        Assert.Equal(account.GetProperty("id").GetGuid(), row.GetProperty("providerAccountId").GetGuid());
        Assert.Equal("Draft", row.GetProperty("applicationStatus").GetString());
        Assert.Equal(3, row.GetProperty("applicationStep").GetInt32());

        var lead = await api.WithDbAsync(db => db.ProviderLeads.SingleAsync(l => l.PhoneNumber == "233247000007"));
        Assert.Equal(pro.User.GetProperty("id").GetGuid(), lead.ConsumerId);
    }

    [Fact]
    public async Task A_phone_that_already_has_a_provider_account_is_told_so()
    {
        var pro = await api.SignInAsync("0247000008", "pro", "Yaw Darko");
        await Onboarding.CreateAccountAsync(pro.Client, "Yaw Darko");

        var data = await Json.ExpectSuccessAsync(await ApplyAsync(Body("0247000008", "Yaw Darko")));
        Assert.True(data.GetProperty("alreadyRegistered").GetBoolean());
        Assert.Equal("Onboarding", (await LeadRowAsync("233247000008")).GetProperty("stage").GetString());
    }

    [Fact]
    public async Task Ops_see_each_lead_at_its_stage_with_counts_by_stage_and_source()
    {
        const string source = "stage-test";
        foreach (var phone in new[] { "0247100001", "0247100002", "0247100003", "0247100004" })
            await Json.ExpectSuccessAsync(await ApplyAsync(Body(phone, source: source)));

        // 0247100001 stays Applied. 0247100002 submits; 0247100003 is rejected.
        foreach (var phone in new[] { "0247100002", "0247100003" })
        {
            var client = (await api.SignInAsync(phone, "pro", "Pro " + phone[^4..])).Client;
            await Onboarding.CompleteAsync(client);
            var application = await Json.ExpectSuccessAsync(await client.PostAsync("/api/v1/provider-applications/me/submit", null));
            if (phone == "0247100003")
            {
                var ops = await Market.OpsAsync(api);
                await Json.ExpectSuccessAsync(await ops.PostAsJsonAsync(
                    $"/api/v1/ops/provider-applications/{application.GetProperty("id").GetGuid()}/reject", new { note = "Card unreadable." }));
            }
        }

        // 0247100004 is approved and takes a job.
        var provider = await Market.ProviderAsync(api, "0247100004");
        var (customer, home) = await Market.CustomerAsync(api, "0547100001");
        await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home));
        var offer = Assert.Single(await Market.OffersAsync(provider.Client));
        await Json.ExpectSuccessAsync(await provider.Client.PostAsync($"/api/v1/job-offers/{offer.GetProperty("id")}/accept", null));

        var list = await LeadsAsync($"?source={source}");
        var stages = list.GetProperty("leads").EnumerateArray()
            .ToDictionary(l => l.GetProperty("phoneNumber").GetString()!, l => l.GetProperty("stage").GetString());
        Assert.Equal(new Dictionary<string, string?>
        {
            ["233247100001"] = "Applied",
            ["233247100002"] = "Submitted",
            ["233247100003"] = "Rejected",
            ["233247100004"] = "FirstJob",
        }, stages);
        Assert.NotEqual(JsonValueKind.Null, list.GetProperty("leads").EnumerateArray()
            .Single(l => l.GetProperty("stage").GetString() == "FirstJob").GetProperty("firstJobAt").ValueKind);

        Assert.Equal(4, list.GetProperty("bySource").GetProperty(source).GetInt32());
        var byStage = list.GetProperty("byStage");
        Assert.Equal(Enum.GetNames<ProviderLeadStage>().ToHashSet(), byStage.EnumerateObject().Select(p => p.Name).ToHashSet());
        Assert.Equal(list.GetProperty("total").GetInt32(), byStage.EnumerateObject().Sum(p => p.Value.GetInt32()));
        Assert.True(byStage.GetProperty("FirstJob").GetInt32() >= 1);

        var submitted = (await LeadsAsync($"?source={source}&stage=Submitted")).GetProperty("leads");
        Assert.Equal("233247100002", Assert.Single(submitted.EnumerateArray()).GetProperty("phoneNumber").GetString());
    }

    [Fact]
    public async Task Ops_mark_a_lead_contacted_and_nobody_else_can()
    {
        await Json.ExpectSuccessAsync(await ApplyAsync(Body("0247000009")));
        var id = (await LeadRowAsync("233247000009")).GetProperty("id").GetGuid();

        var someone = (await api.SignInAsync("0247000010")).Client;
        await Json.ExpectErrorAsync(await someone.GetAsync("/api/v1/ops/provider-leads"), 403, "ops.forbidden");
        await Json.ExpectErrorAsync(await someone.PostAsync($"/api/v1/ops/provider-leads/{id}/contacted", null), 403, "ops.forbidden");

        var ops = await Market.OpsAsync(api);
        var row = await Json.ExpectSuccessAsync(await ops.PostAsJsonAsync($"/api/v1/ops/provider-leads/{id}/contacted",
            new { note = "Called; installing tonight." }));
        Assert.Equal("Called; installing tonight.", row.GetProperty("contactNote").GetString());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("contactedAt").ValueKind);

        await Json.ExpectSuccessAsync(await ops.PostAsync($"/api/v1/ops/provider-leads/{id}/contacted", null));
        await Json.ExpectErrorAsync(await ops.PostAsync($"/api/v1/ops/provider-leads/{Guid.NewGuid()}/contacted", null), 404, "leads.not_found");
    }
}
