using System.Net.Http.Json;
using System.Text.Json;
using Grid.Api.Features.BillPayments;
using Grid.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Tests;

/// <summary>GRID's package prices, the catalogue customers book from, reviews, rebooking and licences.</summary>
public class ServiceBookingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task The_catalogue_lists_launched_services_by_section_with_grid_prices()
    {
        var catalogue = await Onboarding.CatalogueAsync(api.Client(), "service-catalogue");
        var sections = catalogue.GetProperty("sections").EnumerateArray().ToList();
        Assert.Equal(["Cleaning & laundry", "Repairs & maintenance"], sections.Select(s => s.GetProperty("name").GetString()));
        var services = sections.SelectMany(s => s.GetProperty("services").EnumerateArray()).ToList();
        Assert.Equal(["cleaning", "laundry", "ac-servicing", "pest-control"], services.Select(s => s.GetProperty("slug").GetString()));

        var ac = services.Single(s => s.GetProperty("slug").GetString() == "ac-servicing");
        Assert.Equal(150m, ac.GetProperty("fromPrice").GetDecimal());
        var general = ac.GetProperty("packages").EnumerateArray().First();
        Assert.Equal("General service", general.GetProperty("name").GetString());
        Assert.Equal(250m, general.GetProperty("price").GetDecimal());
        Assert.Equal("unit", general.GetProperty("unitLabel").GetString());
        Assert.Equal(6, general.GetProperty("maxQuantity").GetInt32());
        Assert.Contains("Filter wash", general.GetProperty("includes").EnumerateArray().Select(i => i.GetString()));

        // GRID Pro shows providers their share of these prices.
        Assert.Equal(15m, catalogue.GetProperty("feePercent").GetDecimal());
    }

    [Fact]
    public async Task Bookings_are_priced_by_grid_and_validated()
    {
        await Market.ProviderAsync(api, "0243300101", "Labone", service: "ac-servicing");
        var (customer, home) = await Market.CustomerAsync(api, "0543300101", area: "Labone");

        // Three AC units and a deep foam clean on one: priced from GRID's list, whatever the client thinks.
        var general = await Market.PackageIdAsync(customer, "ac-servicing-general-service");
        var foam = await Market.PackageIdAsync(customer, "ac-servicing-deep-foam");
        var booking = await Json.ExpectSuccessAsync(await SendAsync(customer, new
        {
            lines = new[] { new { packageId = general, quantity = 3 }, new { packageId = foam, quantity = 1 } },
            propertyId = home, scheduledAt = api.Time.GetUtcNow().AddHours(3), amount = 1,
        }));
        Assert.Equal(1100m, booking.GetProperty("total").GetDecimal());
        Assert.Equal("AC servicing · 2 items", booking.GetProperty("title").GetString());
        Assert.Equal("3 × General service, Deep foam clean", booking.GetProperty("scope").GetString());
        Assert.Equal(2, booking.GetProperty("lines").GetArrayLength());

        await Json.ExpectErrorAsync(await SendAsync(customer, new
        {
            lines = new[] { new { packageId = general, quantity = 7 } }, propertyId = home, scheduledAt = api.Time.GetUtcNow().AddHours(3),
        }), 400, "validation.failed");
        var small = await Market.PackageIdAsync(customer, "pest-control-fumigation-small");
        var large = await Market.PackageIdAsync(customer, "pest-control-fumigation-large");
        await Json.ExpectErrorAsync(await SendAsync(customer, new
        {
            lines = new[] { new { packageId = small, quantity = 1 }, new { packageId = large, quantity = 1 } },
            propertyId = home, scheduledAt = api.Time.GetUtcNow().AddHours(3),
        }), 400, "validation.failed");
        await Json.ExpectErrorAsync(await SendAsync(customer, new
        {
            lines = new[] { new { packageId = general, quantity = 1 }, new { packageId = small, quantity = 1 } },
            propertyId = home, scheduledAt = api.Time.GetUtcNow().AddHours(3),
        }), 400, "validation.failed");

        // Plumbing isn't launched yet.
        var plumbing = await api.WithDbAsync(db => db.ServicePackages.Where(p => p.Slug == "plumbing-call-out").Select(p => p.Id).SingleAsync());
        await Json.ExpectErrorAsync(await SendAsync(customer, new
        {
            lines = new[] { new { packageId = plumbing, quantity = 1 } }, propertyId = home, scheduledAt = api.Time.GetUtcNow().AddHours(3),
        }), 422, "services.not_launched");

        // The same key returns the same booking.
        var key = Guid.NewGuid().ToString("N");
        var first = await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home, service: "ac-servicing", package: "ac-servicing-general-service", key: key));
        var again = await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home, service: "ac-servicing", package: "ac-servicing-general-service", key: key));
        Assert.Equal(first.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
        var noKey = await customer.PostAsJsonAsync("/api/v1/service-requests", new { });
        await Json.ExpectErrorAsync(noKey, 400, "payments.idempotency_required");
    }

    [Fact]
    public async Task Availability_lists_the_hours_someone_nearby_is_working()
    {
        await Market.ProviderAsync(api, "0243300201", "Achimota", service: "pest-control", workingDays: [Tomorrow().DayOfWeek.ToString()]);
        var (customer, home) = await Market.CustomerAsync(api, "0543300201", area: "Achimota");
        var pest = await Onboarding.CategoryIdAsync(customer, "pest-control");

        var tomorrow = DateOnly.FromDateTime(Tomorrow().UtcDateTime).ToString("yyyy-MM-dd");
        var open = await Json.ExpectSuccessAsync(await customer.GetAsync(
            $"/api/v1/service-requests/availability?serviceCategoryId={pest}&propertyId={home}&date={tomorrow}"));
        Assert.True(open.GetProperty("served").GetBoolean());
        Assert.Equal("Achimota", open.GetProperty("area").GetString());
        Assert.Equal(11, open.GetProperty("slots").GetArrayLength());
        Assert.All(open.GetProperty("slots").EnumerateArray(), s => Assert.True(s.GetProperty("available").GetBoolean()));

        var dayAfter = DateOnly.FromDateTime(Tomorrow().UtcDateTime.AddDays(1)).ToString("yyyy-MM-dd");
        var closed = await Json.ExpectSuccessAsync(await customer.GetAsync(
            $"/api/v1/service-requests/availability?serviceCategoryId={pest}&propertyId={home}&date={dayAfter}"));
        Assert.All(closed.GetProperty("slots").EnumerateArray(), s => Assert.False(s.GetProperty("available").GetBoolean()));
    }

    [Fact]
    public async Task Customers_rate_finished_jobs_once_and_can_ask_for_the_same_provider()
    {
        var pro = await Market.ProviderAsync(api, "0243300301", "Spintex", location: (5.6360, -0.1010));
        var other = await Market.ProviderAsync(api, "0243300302", "Spintex", location: (5.6370, -0.1020));
        var (customer, home) = await Market.CustomerAsync(api, "0543300301", area: "Spintex");
        var requestId = (await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home))).GetProperty("id").GetGuid();

        var offer = (await Market.OffersAsync(pro.Client)).Single();
        var job = await Json.ExpectSuccessAsync(await pro.Client.PostAsync($"/api/v1/job-offers/{offer.GetProperty("id")}/accept", null));
        var jobId = job.GetProperty("id").GetGuid();
        await Json.ExpectErrorAsync(await customer.PostAsJsonAsync($"/api/v1/service-requests/{requestId}/review", new { rating = 5 }), 409, "reviews.not_completed");
        foreach (var step in new[] { "start-journey", "arrive", "start-work", "complete-work" }) await Market.CommandAsync(pro.Client, jobId, step);
        await Json.ExpectSuccessAsync(await customer.PostAsync($"/api/v1/service-requests/{requestId}/confirm-completion", null));

        await Json.ExpectErrorAsync(await customer.PostAsJsonAsync($"/api/v1/service-requests/{requestId}/review", new { rating = 6 }), 400, "validation.failed");
        var reviewed = await Json.ExpectSuccessAsync(await customer.PostAsJsonAsync($"/api/v1/service-requests/{requestId}/review",
            new { rating = 4, comment = "Thorough, a little late." }));
        Assert.Equal(4, reviewed.GetProperty("review").GetProperty("rating").GetInt32());
        Assert.Equal(4.0, reviewed.GetProperty("provider").GetProperty("rating").GetDouble());
        await Json.ExpectErrorAsync(await customer.PostAsJsonAsync($"/api/v1/service-requests/{requestId}/review", new { rating = 5 }), 409, "reviews.already_reviewed");
        var summary = await Json.ExpectSuccessAsync(await pro.Client.GetAsync("/api/v1/provider-accounts/me/summary"));
        Assert.Equal(4.0, summary.GetProperty("rating").GetDouble());
        Assert.Equal(1, summary.GetProperty("reviews").GetInt32());

        // "Book again": the first round goes to that provider alone, even though another is as close.
        await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home, preferred: pro.AccountId));
        Assert.Single(await Market.OffersAsync(pro.Client));
        Assert.Empty(await Market.OffersAsync(other.Client));
    }

    [Fact]
    public async Task Pest_control_needs_an_epa_licence_before_submitting()
    {
        var client = (await api.SignInAsync("0243300401", "pro", "Kojo Pest")).Client;
        await Onboarding.CreateAccountAsync(client, "Kojo Pest");
        var pest = await Onboarding.CategoryIdAsync(client, "pest-control");
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/provider-offerings", new { serviceCategoryId = pest, isActive = true }));
        var readiness = await Json.ExpectSuccessAsync(await client.GetAsync("/api/v1/provider-applications/me/readiness"));
        Assert.Contains("licence.epa_required", readiness.GetProperty("missingRequirements").EnumerateArray().Select(m => m.GetProperty("code").GetString()));

        await Onboarding.AddDocumentAsync(client, "EpaPestControlLicence", "certificates");
        readiness = await Json.ExpectSuccessAsync(await client.GetAsync("/api/v1/provider-applications/me/readiness"));
        Assert.DoesNotContain("licence.epa_required", readiness.GetProperty("missingRequirements").EnumerateArray().Select(m => m.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task Cancelling_a_paid_booking_refunds_it()
    {
        var (customer, home) = await Market.CustomerAsync(api, "0543300501", area: "Osu");
        var booking = await Json.ExpectSuccessAsync(await Market.BookAsync(api, customer, home));
        Assert.True(booking.GetProperty("canCancel").GetBoolean());
        var cancelled = await Json.ExpectSuccessAsync(await customer.PostAsync($"/api/v1/service-requests/{booking.GetProperty("id")}/cancel", null));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal("Refunded", cancelled.GetProperty("payment").GetProperty("status").GetString());
    }

    private DateTimeOffset Tomorrow() => api.Time.GetUtcNow().AddDays(1);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/service-requests") { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }
}

/// <summary>Service payments through Hubtel: nothing is offered until the prompt is approved, and refunds go back through Hubtel.</summary>
public sealed class ServicePaymentTests : IAsyncLifetime
{
    private readonly HubtelSimulator _hubtel = new();
    private PaymentApiFactory _api = null!;

    public async Task InitializeAsync()
    {
        await _hubtel.StartAsync();
        _api = new PaymentApiFactory(_hubtel);
        await _api.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await ((IAsyncLifetime)_api).DisposeAsync();
        await _hubtel.DisposeAsync();
    }

    private async Task PumpAsync()
    {
        var now = _api.Time.GetUtcNow();
        await _api.WithDbAsync(db => db.ServicePayments.Where(p => p.NextCheckAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.NextCheckAt, now)));
        await _api.RunMarketplaceAsync();
    }

    private Task<string> ReferenceAsync(Guid requestId) =>
        _api.WithDbAsync(db => db.ServicePayments.Where(p => p.ServiceRequestId == requestId)
            .OrderByDescending(p => p.CreatedAt).Select(p => p.ClientReference).FirstAsync());

    [Fact]
    public async Task Nothing_is_offered_until_the_momo_prompt_is_approved()
    {
        var pro = await Market.ProviderAsync(_api, "0244400101", "Osu");
        var (customer, home) = await Market.CustomerAsync(_api, "0554400101", area: "Osu");
        var booking = await Json.ExpectSuccessAsync(await Market.BookAsync(_api, customer, home));
        var id = booking.GetProperty("id").GetGuid();
        Assert.Equal("AwaitingPayment", booking.GetProperty("status").GetString());
        Assert.Equal("ApproveOnPhone", booking.GetProperty("payment").GetProperty("nextAction").GetString());
        Assert.Equal(330m, _hubtel.Snapshot.Find(await ReferenceAsync(id)).Amount);
        Assert.Empty(await Market.OffersAsync(pro.Client));

        _hubtel.SetStatus(await ReferenceAsync(id), "Paid", 330m);
        await PumpAsync();
        var paid = await Json.ExpectSuccessAsync(await customer.GetAsync($"/api/v1/service-requests/{id}"));
        Assert.Equal("Searching", paid.GetProperty("status").GetString());
        Assert.Equal("Paid", paid.GetProperty("payment").GetProperty("status").GetString());
        Assert.Single(await Market.OffersAsync(pro.Client));
    }

    [Fact]
    public async Task A_declined_prompt_can_be_paid_again()
    {
        _hubtel.Configure(collection: "reject");
        var (customer, home) = await Market.CustomerAsync(_api, "0554400201", area: "Osu");
        var booking = await Json.ExpectSuccessAsync(await Market.BookAsync(_api, customer, home));
        var id = booking.GetProperty("id").GetGuid();
        Assert.Equal("PaymentFailed", booking.GetProperty("status").GetString());

        _hubtel.Configure(collection: "accept");
        var retried = await Json.ExpectSuccessAsync(await customer.PostAsync($"/api/v1/service-requests/{id}/pay", null));
        Assert.Equal("AwaitingPayment", retried.GetProperty("status").GetString());
        await Json.ExpectErrorAsync(await customer.PostAsync($"/api/v1/service-requests/{id}/pay", null), 409, "services.payment_not_retryable");
    }

    [Fact]
    public async Task An_unfilled_booking_is_refunded_through_hubtel()
    {
        var (customer, home) = await Market.CustomerAsync(_api, "0554400301", area: "Kasoa");
        var id = (await Json.ExpectSuccessAsync(await Market.BookAsync(_api, customer, home))).GetProperty("id").GetGuid();
        var reference = await ReferenceAsync(id);
        _hubtel.SetStatus(reference, "Paid", 330m);
        await PumpAsync();

        // Nobody works in Kasoa: every round finds no one.
        for (var i = 0; i < 5; i++)
        {
            _api.Time.Advance(TimeSpan.FromMinutes(4));
            await PumpAsync();
        }
        customer = await _api.ResumeAsync("0554400301");
        var unfilled = await Json.ExpectSuccessAsync(await customer.GetAsync($"/api/v1/service-requests/{id}"));
        Assert.Equal("Unfilled", unfilled.GetProperty("status").GetString());
        Assert.Equal("Refunding", unfilled.GetProperty("payment").GetProperty("status").GetString());
        Assert.Equal(TxnState.Pending, (await _api.WithDbAsync(db => db.ServicePayments.SingleAsync(p => p.ServiceRequestId == id))).RefundState);

        _hubtel.SetStatus(reference, "Refunded");
        await PumpAsync();
        var refunded = await Json.ExpectSuccessAsync(await customer.GetAsync($"/api/v1/service-requests/{id}"));
        Assert.Equal("Refunded", refunded.GetProperty("payment").GetProperty("status").GetString());
    }
}
