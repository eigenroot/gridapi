using System.Net.Http.Json;
using Grid.Api.Common;
using Grid.Api.Features.Media;
using Grid.Api.Tests.Support;

namespace Grid.Api.Tests;

public class MarketplaceTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    // A point in East Legon, and bases at increasing distance from it.
    private static readonly (double, double) EastLegonHome = (5.6360, -0.1560);
    private static readonly (double, double) NearBase = (5.6400, -0.1600);
    private static readonly (double, double) MadinaBase = (5.6830, -0.1660);
    private static readonly (double, double) TemaBase = (5.6698, -0.0166);

    [Fact]
    public async Task Offers_go_to_the_nearest_providers_and_report_distance_and_earnings()
    {
        var near = await Market.ProviderAsync(api, "0241100001", "East Legon", location: NearBase);
        var madina = await Market.ProviderAsync(api, "0241100002", "Madina", location: MadinaBase, travelRadiusKm: 10);
        var tema = await Market.ProviderAsync(api, "0241100003", "Tema", location: TemaBase, travelRadiusKm: 5);
        var offline = await Market.ProviderAsync(api, "0241100004", "East Legon", location: NearBase, online: false);
        var laundry = await Market.ProviderAsync(api, "0241100005", "East Legon", service: "laundry", location: NearBase);

        var (customer, home) = await Market.CustomerAsync(api, "0541100001", location: EastLegonHome);
        var booked = await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home, package: "cleaning-deep-3-bed"));
        Assert.Equal("Searching", booked.GetProperty("status").GetString());
        Assert.Equal("Deep clean · 3 bedrooms", booked.GetProperty("title").GetString());
        Assert.Equal(590m, booked.GetProperty("total").GetDecimal());
        Assert.Equal("Paid", booked.GetProperty("payment").GetProperty("status").GetString());
        Assert.StartsWith("GR-", booked.GetProperty("reference").GetString());

        var nearOffer = Assert.Single(await Market.OffersAsync(near.Client));
        var madinaOffer = Assert.Single(await Market.OffersAsync(madina.Client));
        Assert.Empty(await Market.OffersAsync(tema.Client));      // 15 km away with a 5 km radius
        Assert.Empty(await Market.OffersAsync(offline.Client));   // not online
        Assert.Empty(await Market.OffersAsync(laundry.Client));   // wrong service

        Assert.True(nearOffer.GetProperty("distanceKm").GetDecimal() < 1.5m);
        Assert.InRange(madinaOffer.GetProperty("distanceKm").GetDecimal(), 4m, 7m);
        // GRID's price for a 3-bedroom deep clean, less GRID's 15%.
        Assert.Equal(590m, nearOffer.GetProperty("price").GetDecimal());
        Assert.Equal(88.5m, nearOffer.GetProperty("fee").GetDecimal());
        Assert.Equal(501.5m, nearOffer.GetProperty("earnings").GetDecimal());
        // Nothing that identifies the customer before acceptance.
        Assert.Equal("East Legon", nearOffer.GetProperty("generalArea").GetString());
        Assert.False(nearOffer.TryGetProperty("address", out _));
        Assert.False(nearOffer.TryGetProperty("customerName", out _));
    }

    [Fact]
    public async Task Without_locations_matching_falls_back_to_the_areas_providers_cover()
    {
        var covers = await Market.ProviderAsync(api, "0242200001", "Osu");
        var elsewhere = await Market.ProviderAsync(api, "0242200002", "Kasoa");

        var (customer, home) = await Market.CustomerAsync(api, "0542200001", area: "osu");
        await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home));

        var offer = Assert.Single(await Market.OffersAsync(covers.Client));
        Assert.Equal(0m, offer.GetProperty("distanceKm").GetDecimal());
        Assert.Empty(await Market.OffersAsync(elsewhere.Client));
    }

    [Fact]
    public async Task A_home_outside_every_area_cannot_be_booked()
    {
        var (customer, home) = await Market.CustomerAsync(api, "0543300001", area: "Bolgatanga");
        await Json.ExpectErrorAsync(await Market.BookAsync(api, customer, home), 422, "services.area_not_served");

        var located = await Json.ExpectSuccessAsync(await customer.GetAsync("/api/v1/service-areas/locate?latitude=5.6360&longitude=-0.1560"));
        Assert.True(located.GetProperty("served").GetBoolean());
        Assert.Equal("East Legon", located.GetProperty("area").GetProperty("name").GetString());
        var nowhere = await Json.ExpectSuccessAsync(await customer.GetAsync("/api/v1/service-areas/locate?latitude=10.7856&longitude=-0.8514"));
        Assert.False(nowhere.GetProperty("served").GetBoolean());
    }

    [Fact]
    public async Task Providers_who_do_not_work_that_day_are_skipped()
    {
        var scheduledAt = api.Time.GetUtcNow().AddHours(3);
        var otherDay = scheduledAt.AddDays(1).DayOfWeek.ToString();
        var working = await Market.ProviderAsync(api, "0244400001", "Labone", workingDays: [scheduledAt.DayOfWeek.ToString()]);
        var resting = await Market.ProviderAsync(api, "0244400002", "Labone", workingDays: [otherDay]);

        var (customer, home) = await Market.CustomerAsync(api, "0544400001", area: "Labone");
        await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home, scheduledAt: scheduledAt));

        Assert.Single(await Market.OffersAsync(working.Client));
        Assert.Empty(await Market.OffersAsync(resting.Client));
    }

    [Fact]
    public async Task First_acceptance_wins_and_a_cleaning_job_runs_through_to_a_payout()
    {
        var first = await Market.ProviderAsync(api, "0245500001", "Cantonments");
        var second = await Market.ProviderAsync(api, "0245500002", "Cantonments");
        var (customer, home) = await Market.CustomerAsync(api, "0545500001", area: "Cantonments");
        var booking = await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home));
        var requestId = booking.GetProperty("id").GetGuid();

        var offer = Assert.Single(await Market.OffersAsync(first.Client));
        var losing = Assert.Single(await Market.OffersAsync(second.Client));
        var job = await Json.ExpectSuccessAsync(await first.Client.PostAsync($"/api/v1/job-offers/{offer.GetProperty("id")}/accept", null));
        var jobId = job.GetProperty("id").GetGuid();
        Assert.StartsWith("GJ-", job.GetProperty("jobCode").GetString());
        Assert.Equal("accepted", job.GetProperty("stage").GetString());
        Assert.Equal("start-journey", job.GetProperty("nextCommand").GetString());
        Assert.Equal("Ama Customer", job.GetProperty("customerName").GetString());
        Assert.Equal("12 Lagos Avenue", job.GetProperty("address").GetString());
        Assert.Equal("233545500001", job.GetProperty("customerPhone").GetString());

        // Accepting again returns the same job; the other provider is told it's gone.
        var again = await Json.ExpectSuccessAsync(await first.Client.PostAsync($"/api/v1/job-offers/{offer.GetProperty("id")}/accept", null));
        Assert.Equal(jobId, again.GetProperty("id").GetGuid());
        await Json.ExpectErrorAsync(await second.Client.PostAsync($"/api/v1/job-offers/{losing.GetProperty("id")}/accept", null),
            409, "jobs.offer_already_accepted");
        Assert.Empty(await Market.OffersAsync(second.Client));

        var seen = await Json.ExpectSuccessAsync(await customer.GetAsync($"/api/v1/service-requests/{requestId}"));
        Assert.Equal("Assigned", seen.GetProperty("status").GetString());
        Assert.Equal("Pro 0001", seen.GetProperty("provider").GetProperty("displayName").GetString());

        // Steps can't be skipped, and repeating the last one is harmless.
        await Json.ExpectErrorAsync(await first.Client.PostAsync($"/api/v1/jobs/{jobId}/complete-work", null), 409, "jobs.invalid_transition");
        Assert.Equal("onTheWay", (await Market.CommandAsync(first.Client, jobId, "start-journey")).GetProperty("stage").GetString());
        Assert.Equal("onTheWay", (await Market.CommandAsync(first.Client, jobId, "start-journey")).GetProperty("stage").GetString());
        await Json.ExpectErrorAsync(await customer.PostAsync($"/api/v1/service-requests/{requestId}/cancel", null), 409, "services.request_not_cancellable");
        await Market.CommandAsync(first.Client, jobId, "arrive");
        await Market.CommandAsync(first.Client, jobId, "start-work");
        var done = await Market.CommandAsync(first.Client, jobId, "complete-work");
        Assert.Equal("awaitingConfirmation", done.GetProperty("stage").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, done.GetProperty("nextCommand").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, done.GetProperty("customerPhone").ValueKind);

        var summary = await Json.ExpectSuccessAsync(await first.Client.GetAsync("/api/v1/provider-earnings/summary"));
        Assert.Equal(280.5m, summary.GetProperty("pendingAmount").GetDecimal());
        Assert.Equal(0m, summary.GetProperty("availableAmount").GetDecimal());

        // Only the customer (or Ops) closes the job; that releases the earning.
        await Json.ExpectErrorAsync(await first.Client.PostAsync($"/api/v1/service-requests/{requestId}/confirm-completion", null),
            404, "services.request_not_found");
        var closed = await Json.ExpectSuccessAsync(await customer.PostAsync($"/api/v1/service-requests/{requestId}/confirm-completion", null));
        Assert.Equal("Completed", closed.GetProperty("status").GetString());
        summary = await Json.ExpectSuccessAsync(await first.Client.GetAsync("/api/v1/provider-earnings/summary"));
        Assert.Equal(280.5m, summary.GetProperty("availableAmount").GetDecimal());
        Assert.Equal(1, summary.GetProperty("jobsCount").GetInt32());

        var ops = await Market.OpsAsync(api);
        await Json.ExpectErrorAsync(await first.Client.PostAsJsonAsync("/api/v1/ops/provider-payouts", new { providerAccountId = first.AccountId }),
            403, "ops.forbidden");
        var payout = await Json.ExpectSuccessAsync(await ops.PostAsJsonAsync("/api/v1/ops/provider-payouts",
            new { providerAccountId = first.AccountId, externalReference = "HUB-123" }));
        Assert.Equal(280.5m, payout.GetProperty("amount").GetDecimal());
        await Json.ExpectErrorAsync(await ops.PostAsJsonAsync("/api/v1/ops/provider-payouts", new { providerAccountId = first.AccountId }),
            409, "payouts.nothing_available");

        summary = await Json.ExpectSuccessAsync(await first.Client.GetAsync("/api/v1/provider-earnings/summary"));
        Assert.Equal(0m, summary.GetProperty("availableAmount").GetDecimal());
        Assert.Equal(280.5m, summary.GetProperty("paidAmount").GetDecimal());
        var earning = Assert.Single((await Json.ExpectSuccessAsync(await first.Client.GetAsync("/api/v1/provider-earnings"))).EnumerateArray());
        Assert.Equal("Paid", earning.GetProperty("status").GetString());
        var listed = Assert.Single((await Json.ExpectSuccessAsync(await first.Client.GetAsync("/api/v1/provider-payouts"))).EnumerateArray());
        Assert.StartsWith("PO-", listed.GetProperty("reference").GetString());
        Assert.Contains("MTN Mobile Money", listed.GetProperty("destination").GetString());

        var history = (await Json.ExpectSuccessAsync(await first.Client.GetAsync("/api/v1/jobs?status=completed"))).EnumerateArray();
        Assert.Equal("completed", Assert.Single(history).GetProperty("stage").GetString());
        var profile = await Json.ExpectSuccessAsync(await first.Client.GetAsync("/api/v1/provider-accounts/me/summary"));
        Assert.Equal(1, profile.GetProperty("completedJobs").GetInt32());
    }

    [Fact]
    public async Task Laundry_follows_pickup_and_delivery_and_is_confirmed_automatically()
    {
        var pro = await Market.ProviderAsync(api, "0246600001", "Dansoman", service: "laundry", price: 60m);
        var (customer, home) = await Market.CustomerAsync(api, "0546600001", area: "Dansoman");
        await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home, service: "laundry", quantity: 2));

        var offer = Assert.Single(await Market.OffersAsync(pro.Client));
        Assert.Equal(120m, offer.GetProperty("price").GetDecimal());
        Assert.Equal(2, offer.GetProperty("items").GetArrayLength());
        var job = await Json.ExpectSuccessAsync(await pro.Client.PostAsync($"/api/v1/job-offers/{offer.GetProperty("id")}/accept", null));
        var jobId = job.GetProperty("id").GetGuid();
        Assert.Equal("PickupAndDelivery", job.GetProperty("workflow").GetString());

        foreach (var command in new[] { "confirm-pickup", "start-processing", "mark-ready", "start-delivery", "confirm-delivery" })
        {
            Assert.Equal(command, (await Json.ExpectSuccessAsync(await pro.Client.GetAsync($"/api/v1/jobs/{jobId}"))).GetProperty("nextCommand").GetString());
            await Market.CommandAsync(pro.Client, jobId, command);
        }

        // The customer never confirms; a day later the worker does it for them.
        await api.RunMarketplaceAsync();
        Assert.Equal(0m, (await Json.ExpectSuccessAsync(await pro.Client.GetAsync("/api/v1/provider-earnings/summary"))).GetProperty("availableAmount").GetDecimal());
        api.Time.Advance(TimeSpan.FromHours(25));
        pro = pro with { Client = await api.ResumeAsync(pro.Phone, "pro") };
        await api.RunMarketplaceAsync();
        Assert.Equal(102m, (await Json.ExpectSuccessAsync(await pro.Client.GetAsync("/api/v1/provider-earnings/summary"))).GetProperty("availableAmount").GetDecimal());
    }

    [Fact]
    public async Task Declines_and_lapsed_offers_move_the_request_on_until_it_is_unfilled()
    {
        var first = await Market.ProviderAsync(api, "0247700001", "Teshie");
        var (customer, home) = await Market.CustomerAsync(api, "0547700001", area: "Teshie");
        var booking = await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home, scheduledAt: api.Time.GetUtcNow().AddDays(2)));
        var requestId = booking.GetProperty("id").GetGuid();

        var offer = Assert.Single(await Market.OffersAsync(first.Client));
        await Json.ExpectSuccessAsync(await first.Client.PostAsJsonAsync($"/api/v1/job-offers/{offer.GetProperty("id")}/decline", new { reason = "Too far" }));
        Assert.Empty(await Market.OffersAsync(first.Client));
        await Json.ExpectErrorAsync(await first.Client.PostAsync($"/api/v1/job-offers/{offer.GetProperty("id")}/accept", null), 409, "jobs.offer_expired");

        // Someone new comes online; the next round finds them, and they let it lapse.
        var second = await Market.ProviderAsync(api, "0247700002", "Teshie");
        api.Time.Advance(TimeSpan.FromMinutes(4));
        await api.RunMarketplaceAsync();
        second = second with { Client = await api.ResumeAsync(second.Phone, "pro") };
        var lapsing = Assert.Single(await Market.OffersAsync(second.Client));
        api.Time.Advance(TimeSpan.FromMinutes(11));
        await api.RunMarketplaceAsync();
        Assert.Empty(await Market.OffersAsync(second.Client));
        await Json.ExpectErrorAsync(await second.Client.PostAsync($"/api/v1/job-offers/{lapsing.GetProperty("id")}/accept", null), 409, "jobs.offer_expired");

        // Nobody left: after the remaining rounds the booking is handed to Ops.
        for (var i = 0; i < 4; i++)
        {
            api.Time.Advance(TimeSpan.FromMinutes(4));
            await api.RunMarketplaceAsync();
        }
        customer = await api.ResumeAsync("0547700001");
        Assert.Equal("Unfilled", (await Json.ExpectSuccessAsync(await customer.GetAsync($"/api/v1/service-requests/{requestId}"))).GetProperty("status").GetString());

        var ops = await Market.OpsAsync(api);
        var queue = (await Json.ExpectSuccessAsync(await ops.GetAsync("/api/v1/ops/service-requests"))).EnumerateArray();
        Assert.Contains(queue, r => r.GetProperty("id").GetGuid() == requestId && r.GetProperty("offersSent").GetInt32() == 2);
        Assert.Equal("Cancelled", (await Json.ExpectSuccessAsync(await customer.PostAsync($"/api/v1/service-requests/{requestId}/cancel", null))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_provider_who_backs_out_before_setting_off_returns_the_booking_to_the_pool()
    {
        var first = await Market.ProviderAsync(api, "0248800001", "Nungua", location: (5.6010, -0.0770));
        var second = await Market.ProviderAsync(api, "0248800002", "Nungua", location: (5.6100, -0.0800));
        var (customer, home) = await Market.CustomerAsync(api, "0548800001", area: "Nungua");
        var requestId = (await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home))).GetProperty("id").GetGuid();

        var offer = Assert.Single(await Market.OffersAsync(first.Client));
        var job = await Json.ExpectSuccessAsync(await first.Client.PostAsync($"/api/v1/job-offers/{offer.GetProperty("id")}/accept", null));
        Assert.Empty(await Market.OffersAsync(second.Client));   // withdrawn when the first accepted

        await Json.ExpectErrorAsync(await first.Client.PostAsJsonAsync($"/api/v1/jobs/{job.GetProperty("id")}/cancel", new { }), 400, "validation.failed");
        var cancelled = await Json.ExpectSuccessAsync(await first.Client.PostAsJsonAsync($"/api/v1/jobs/{job.GetProperty("id")}/cancel", new { reason = "Family emergency" }));
        Assert.Equal("cancelled", cancelled.GetProperty("stage").GetString());

        // The second provider already had their one offer, so nobody is left and the search goes on.
        Assert.Equal("Searching", (await Json.ExpectSuccessAsync(await customer.GetAsync($"/api/v1/service-requests/{requestId}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Going_online_needs_approval_and_a_payout_method()
    {
        var client = (await api.SignInAsync("0249900001", "pro", "Kofi Mensah")).Client;
        await Onboarding.CompleteAsync(client);
        var blocked = await Json.ExpectErrorAsync(await client.PutAsJsonAsync("/api/v1/provider-accounts/me/operating-status",
            new { operatingStatus = "Online" }), 409, "providers.not_job_ready");
        var codes = blocked.GetProperty("detail").GetProperty("missingRequirements").EnumerateArray().Select(m => m.GetProperty("code").GetString()).ToList();
        Assert.Equal(["verification.required", "payout.required"], codes);

        var application = await Json.ExpectSuccessAsync(await client.PostAsync("/api/v1/provider-applications/me/submit", null));
        var applicationId = application.GetProperty("id").GetGuid();
        var ops = await Market.OpsAsync(api);
        await Json.ExpectErrorAsync(await client.GetAsync("/api/v1/ops/provider-applications"), 403, "ops.forbidden");
        var queue = (await Json.ExpectSuccessAsync(await ops.GetAsync("/api/v1/ops/provider-applications"))).EnumerateArray();
        Assert.Contains(queue, a => a.GetProperty("applicationId").GetGuid() == applicationId);

        var detail = await Json.ExpectSuccessAsync(await ops.GetAsync($"/api/v1/ops/provider-applications/{applicationId}"));
        Assert.Equal(3, detail.GetProperty("documents").GetArrayLength());
        var document = detail.GetProperty("documents")[0];
        var file = await ops.GetAsync($"/api/v1/ops/media/{document.GetProperty("storageKey").GetString()}");
        Assert.Equal("image/jpeg", file.Content.Headers.ContentType?.MediaType);

        // Sent back, the provider can edit and resubmit; approved, they can go online once paid details exist.
        await Json.ExpectErrorAsync(await ops.PostAsJsonAsync($"/api/v1/ops/provider-applications/{applicationId}/request-information", new { }), 400, "validation.failed");
        var returned = await Json.ExpectSuccessAsync(await ops.PostAsJsonAsync(
            $"/api/v1/ops/provider-applications/{applicationId}/request-information", new { note = "Retake the card photo." }));
        Assert.Equal("MoreInformationRequired", returned.GetProperty("status").GetString());
        await Json.ExpectSuccessAsync(await client.PutAsJsonAsync("/api/v1/provider-accounts/me", Onboarding.Details()));
        await Json.ExpectSuccessAsync(await client.PostAsync("/api/v1/provider-applications/me/submit", null));
        await Json.ExpectSuccessAsync(await ops.PostAsync($"/api/v1/ops/provider-applications/{applicationId}/start-review", null));
        var approved = await Json.ExpectSuccessAsync(await ops.PostAsync($"/api/v1/ops/provider-applications/{applicationId}/approve", null));
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        await Json.ExpectErrorAsync(await ops.PostAsJsonAsync($"/api/v1/ops/provider-applications/{applicationId}/reject", new { note = "No." }),
            409, "ops.application_not_reviewable");

        await Json.ExpectErrorAsync(await client.PutAsJsonAsync("/api/v1/provider-accounts/me/operating-status",
            new { operatingStatus = "Online" }), 409, "providers.not_job_ready");
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/payout-methods", new
        {
            methodType = "MobileMoney", providerName = "MTN Mobile Money", accountName = "Kofi Mensah", accountNumber = "0249900001",
        }));
        var summary = await Json.ExpectSuccessAsync(await client.PutAsJsonAsync("/api/v1/provider-accounts/me/operating-status",
            new { operatingStatus = "Online" }));
        Assert.Equal("Online", summary.GetProperty("operatingStatus").GetString());
        Assert.True(summary.GetProperty("canGoOnline").GetBoolean());
        Assert.Equal("Kofi Mensah", summary.GetProperty("ownerName").GetString());
        Assert.Equal("233249900001", summary.GetProperty("phoneNumber").GetString());
        Assert.Equal(["East Legon"], summary.GetProperty("serviceAreas").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("MTN Mobile Money", summary.GetProperty("payoutMethod").GetProperty("providerName").GetString());

        // Approved providers run their own prices, but can't swap the service GRID approved.
        var offering = summary.GetProperty("offerings")[0];
        var cleaning = await Onboarding.CategoryIdAsync(client, "cleaning");
        var laundry = await Onboarding.CategoryIdAsync(client, "laundry");
        var repriced = await Json.ExpectSuccessAsync(await client.PutAsJsonAsync(
            $"/api/v1/provider-offerings/{offering.GetProperty("id")}", Onboarding.Offering(cleaning, 95m)));
        Assert.Equal(95m, repriced.GetProperty("unitPrice").GetDecimal());
        await Json.ExpectErrorAsync(await client.PutAsJsonAsync(
            $"/api/v1/provider-offerings/{offering.GetProperty("id")}", Onboarding.Offering(laundry, 95m)), 409, "providers.locked");
        await Json.ExpectErrorAsync(await client.PutAsJsonAsync("/api/v1/provider-accounts/me", Onboarding.Details()), 409, "providers.locked");
        var edited = await Json.ExpectSuccessAsync(await client.PutAsJsonAsync("/api/v1/provider-accounts/me/profile", new { description = "Ten years of spotless homes." }));
        Assert.Equal("Ten years of spotless homes.", edited.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Support_tickets_are_numbered_and_only_reference_the_callers_own_jobs()
    {
        var caller = (await api.SignInAsync("0240010001", "pro")).Client;
        var ticket = await Json.ExpectSuccessAsync(await caller.PostAsJsonAsync("/api/v1/support-tickets",
            new { category = "Payment", subject = "Payment or payout issue", details = "My payout hasn't arrived." }));
        Assert.StartsWith("GRID-", ticket.GetProperty("reference").GetString());
        Assert.Equal("Open", ticket.GetProperty("status").GetString());
        await Json.ExpectErrorAsync(await caller.PostAsJsonAsync("/api/v1/support-tickets",
            new { subject = "Job", details = "Not mine.", jobId = Guid.NewGuid() }), 404, "jobs.not_found");
        Assert.Single((await Json.ExpectSuccessAsync(await caller.GetAsync("/api/v1/support-tickets"))).EnumerateArray());

        var ops = await Market.OpsAsync(api);
        var resolved = await Json.ExpectSuccessAsync(await ops.PostAsJsonAsync(
            $"/api/v1/ops/support-tickets/{ticket.GetProperty("id")}/resolve", new { note = "Paid on Friday." }));
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
    }

    [Fact]
    public void Distance_is_measured_along_the_great_circle()
    {
        // Accra to Kumasi is about 200 km in a straight line.
        var km = Geo.DistanceKm(new GeoPoint(5.6037, -0.1870), new GeoPoint(6.6885, -1.6244));
        Assert.InRange(km, 195, 205);
        Assert.Equal(0, Geo.DistanceKm(new GeoPoint(5.6, -0.2), new GeoPoint(5.6, -0.2)), 6);
    }

    [Fact]
    public void Presigned_urls_match_the_published_signature_v4_example()
    {
        // The worked example from the Amazon S3 "Authenticating Requests: Using Query Parameters" guide.
        var url = SigV4.PresignUrl(
            "GET", "https", "examplebucket.s3.amazonaws.com", "/test.txt",
            "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "us-east-1",
            new DateTimeOffset(2013, 5, 24, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromSeconds(86400));

        Assert.Equal(
            "https://examplebucket.s3.amazonaws.com/test.txt"
            + "?X-Amz-Algorithm=AWS4-HMAC-SHA256"
            + "&X-Amz-Credential=AKIAIOSFODNN7EXAMPLE%2F20130524%2Fus-east-1%2Fs3%2Faws4_request"
            + "&X-Amz-Date=20130524T000000Z&X-Amz-Expires=86400&X-Amz-SignedHeaders=host"
            + "&X-Amz-Signature=aeeed9bbccd4d02ee5c0109b86d86835f995330da4c265957d157751f604d404",
            url);
    }

    [Fact]
    public void R2_upload_links_go_straight_to_the_bucket_and_bind_type_and_size()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new MediaOptions
        {
            Provider = MediaProvider.R2,
            R2 = new R2Options { AccountId = "abc123", AccessKeyId = "key", SecretAccessKey = "secret", Bucket = "grid-media" },
        });
        var store = new R2MediaStore(new HttpClient(), options, api.Time);
        const string key = "0123456789abcdef0123456789abcdef.jpg";

        var target = store.DirectUpload(key, "image/jpeg", 2048, api.Time.GetUtcNow().AddMinutes(15))!;
        var url = new Uri(target.Url);
        Assert.Equal("abc123.r2.cloudflarestorage.com", url.Host);
        Assert.Equal($"/grid-media/uploads/{key}", url.AbsolutePath);
        Assert.Contains("X-Amz-SignedHeaders=content-length%3Bcontent-type%3Bhost", url.Query);
        Assert.Contains("X-Amz-Expires=900", url.Query);
        Assert.Equal("image/jpeg", target.Headers["Content-Type"]);
        Assert.Equal("2048", target.Headers["Content-Length"]);
    }
}
