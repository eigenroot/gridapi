using System.Net.Http.Json;
using System.Text.Json;

namespace Grid.Api.Tests.Support;

/// <summary>Builds the two sides of a booking: approved, online providers and customers with homes.</summary>
public static class Market
{
    public sealed record Provider(HttpClient Client, Guid AccountId, string Phone);

    /// <summary>
    /// Takes someone through onboarding, has Ops approve them, and puts them online. Available
    /// every day, all day, unless <paramref name="workingDays"/> says otherwise.
    /// </summary>
    public static async Task<Provider> ProviderAsync(
        ApiFactory api, string phone, string area = "East Legon", string service = "cleaning", decimal price = 80m,
        (double Lat, double Lng)? location = null, decimal? travelRadiusKm = null, bool online = true,
        string[]? workingDays = null)
    {
        var signedIn = await api.SignInAsync(phone, "pro", "Pro " + phone[^4..]);
        var client = signedIn.Client;
        var account = await Onboarding.CreateAccountAsync(client, "Pro " + phone[^4..]);
        await Json.ExpectSuccessAsync(await client.PutAsJsonAsync("/api/v1/provider-accounts/me", Onboarding.Details("Pro " + phone[^4..])));

        var categoryId = await Onboarding.CategoryIdAsync(client, service);
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/provider-offerings", new
        {
            serviceCategoryId = categoryId,
            pricingMode = service == "laundry" ? "StartingFrom" : "PerUnit",
            startingPrice = service == "laundry" ? price : (decimal?)null,
            unitPrice = service == "laundry" ? (decimal?)null : price,
            unitName = service == "laundry" ? null : "hour",
            supportsSameDay = true,
            isActive = true,
        }));
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/provider-service-areas", new
        {
            serviceAreaId = await Onboarding.AreaIdAsync(client, area),
            maximumTravelDistanceKm = (decimal?)null,
        }));

        var days = workingDays ?? ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
        await Json.ExpectSuccessAsync(await client.PutAsJsonAsync("/api/v1/provider-availability/rules", new
        {
            rules = days.Select(day => new { dayOfWeek = day, startTime = "00:00:00", endTime = "23:59:59", isAvailable = true }),
        }));
        await Onboarding.AddDocumentAsync(client, "GhanaCard");
        await Onboarding.AddDocumentAsync(client, "GhanaCardBack");
        await Onboarding.AddDocumentAsync(client, "Selfie");
        if (service == "pest-control") await Onboarding.AddDocumentAsync(client, "EpaPestControlLicence", "certificates");
        await Json.ExpectSuccessAsync(await client.PostAsync("/api/v1/provider-standards/current/accept", null));
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/payout-methods", new
        {
            methodType = "MobileMoney", providerName = "MTN Mobile Money", accountName = "Pro", accountNumber = phone, makeDefault = true,
        }));
        var application = await Json.ExpectSuccessAsync(await client.PostAsync("/api/v1/provider-applications/me/submit", null));

        var ops = await OpsAsync(api);
        await Json.ExpectSuccessAsync(await ops.PostAsync(
            $"/api/v1/ops/provider-applications/{application.GetProperty("id").GetGuid()}/approve", null));

        if (location is not null || travelRadiusKm is not null)
        {
            await Json.ExpectSuccessAsync(await client.PutAsJsonAsync("/api/v1/provider-accounts/me/location", new
            {
                latitude = location?.Lat, longitude = location?.Lng, travelRadiusKm,
            }));
        }
        if (online)
        {
            await Json.ExpectSuccessAsync(await client.PutAsJsonAsync("/api/v1/provider-accounts/me/operating-status",
                new { operatingStatus = "Online" }));
        }
        return new Provider(client, account.GetProperty("id").GetGuid(), phone);
    }

    public static Task<HttpClient> OpsAsync(ApiFactory api) => api.ResumeAsync(ApiFactory.OpsPhone);

    /// <summary>A signed-in customer with one home. Returns the client and the home's id.</summary>
    public static async Task<(HttpClient Client, Guid PropertyId)> CustomerAsync(
        ApiFactory api, string phone, string area = "East Legon", (double Lat, double Lng)? location = null)
    {
        var client = (await api.SignInAsync(phone, "consumer", "Ama Customer")).Client;
        var home = await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/properties", new
        {
            nickname = "Home", kind = "House", area, city = "Accra", address = "12 Lagos Avenue",
            latitude = location?.Lat, longitude = location?.Lng,
        }));
        return (client, home.GetProperty("id").GetGuid());
    }

    /// <summary>A package's id by its slug, e.g. <c>cleaning-standard-3-bed</c>.</summary>
    public static async Task<Guid> PackageIdAsync(HttpClient client, string slug)
    {
        var catalogue = await Onboarding.CatalogueAsync(client, "service-catalogue");
        return catalogue.GetProperty("sections").EnumerateArray()
            .SelectMany(s => s.GetProperty("services").EnumerateArray())
            .SelectMany(s => s.GetProperty("packages").EnumerateArray())
            .Single(p => p.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Books one package (by default a 3-bedroom standard clean, GH₵ 330) or, for laundry,
    /// <paramref name="quantity"/> bags of wash &amp; fold.
    /// </summary>
    public static async Task<HttpResponseMessage> BookAsync(
        ApiFactory api, HttpClient customer, Guid propertyId, string service = "cleaning", int quantity = 1,
        DateTimeOffset? scheduledAt = null, string? package = null, string? key = null, Guid? preferred = null)
    {
        var slug = package ?? (service == "laundry" ? "laundry-wash-fold" : $"{service}-standard-3-bed");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/service-requests")
        {
            Content = JsonContent.Create(new
            {
                lines = new[] { new { packageId = await PackageIdAsync(customer, slug), quantity } },
                propertyId,
                scheduledAt = scheduledAt ?? api.Time.GetUtcNow().AddHours(3),
                notes = "Gate code 2468.",
                items = service == "laundry" ? new[] { "8 shirts", "1 duvet" } : [],
                preferredProviderAccountId = preferred,
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await customer.SendAsync(request);
    }

    public static async Task<List<JsonElement>> OffersAsync(HttpClient provider) =>
        (await Json.ExpectSuccessAsync(await provider.GetAsync("/api/v1/job-offers"))).EnumerateArray().ToList();

    public static async Task<JsonElement> CommandAsync(HttpClient provider, Guid jobId, string command) =>
        await Json.ExpectSuccessAsync(await provider.PostAsync($"/api/v1/jobs/{jobId}/{command}", null));
}
