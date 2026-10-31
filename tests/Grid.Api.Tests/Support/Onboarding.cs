using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Grid.Api.Tests.Support;

/// <summary>
/// The calls GRID Pro makes during onboarding, with the same bodies its sync layer sends
/// (<c>GRID PRO/lib/data/api/onboarding_sync.dart</c>), so these tests hold the server to the client.
/// </summary>
public static class Onboarding
{
    public static byte[] Jpeg(int size = 256)
    {
        var bytes = new byte[size];
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF; bytes[3] = 0xE0;
        return bytes;
    }

    public static byte[] Png(int size = 256)
    {
        var bytes = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        return bytes;
    }

    public static async Task<JsonElement> CatalogueAsync(HttpClient client, string path) =>
        await Json.ExpectSuccessAsync(await client.GetAsync($"/api/v1/{path}"));

    public static async Task<Guid> CategoryIdAsync(HttpClient client, string slug) =>
        (await CatalogueAsync(client, "service-categories")).EnumerateArray()
        .Single(c => c.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid();

    public static async Task<Guid> AreaIdAsync(HttpClient client, string name) =>
        (await CatalogueAsync(client, "service-areas")).EnumerateArray()
        .Single(a => a.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    public static async Task<JsonElement> CreateAccountAsync(HttpClient client, string displayName = "Akosua Boateng") =>
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/provider-accounts",
            new { accountType = "Individual", displayName }));

    public static object Details(string displayName = "Akosua Boateng", string deliveryMode = "TravelsToCustomer", string? premisesAddress = null) => new
    {
        displayName,
        legalName = (string?)null,
        description = "I clean homes in East Legon.",
        email = (string?)null,
        profileImageUrl = (string?)null,
        businessRegistrationNumber = (string?)null,
        city = "Accra",
        primaryArea = "East Legon",
        yearsOfExperience = 4,
        deliveryMode,
        premisesAddress,
    };

    /// <summary>The offering body for cleaning priced hourly, or unpriced as the services step first sends it.</summary>
    public static object Offering(Guid categoryId, decimal? hourlyRate) => new
    {
        serviceCategoryId = categoryId,
        customServiceName = (string?)null,
        description = "Deep cleaning, Window cleaning",
        pricingMode = hourlyRate is null ? "InspectionThenQuote" : "PerUnit",
        fixedPrice = (decimal?)null,
        startingPrice = (decimal?)null,
        inspectionFee = (decimal?)null,
        unitPrice = hourlyRate,
        unitName = hourlyRate is null ? null : "hour",
        supportsEmergency = false,
        supportsSameDay = true,
        isActive = true,
    };

    public static object Availability(params string[] workingDays) => new
    {
        rules = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }
            .Select(day => new { dayOfWeek = day, startTime = "08:00:00", endTime = "17:00:00", isAvailable = workingDays.Contains(day) }),
    };

    /// <summary>Request a ticket, PUT the bytes to its absolute URL, confirm. Returns the storage key.</summary>
    public static async Task<string> UploadAsync(
        HttpClient client, string purpose, byte[] bytes, string contentType = "image/jpeg", string fileName = "photo.jpg")
    {
        var ticket = await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/media/upload-requests",
            new { purpose, fileName, contentType, sizeBytes = bytes.Length }));
        await Json.ExpectSuccessAsync(await PutBytesAsync(client, ticket.GetProperty("uploadUrl").GetString()!, bytes, contentType));
        var key = ticket.GetProperty("storageKey").GetString()!;
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/media/uploads/confirm", new { storageKey = key }));
        return key;
    }

    public static Task<HttpResponseMessage> PutBytesAsync(HttpClient client, string url, byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return client.PutAsync(url, content);
    }

    public static async Task AddDocumentAsync(HttpClient client, string documentType, string purpose = "identity")
    {
        var key = await UploadAsync(client, purpose, Jpeg());
        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/provider-verification-documents",
            new { documentType, storageKey = key, fileName = "photo.jpg", contentType = "image/jpeg", sizeBytes = 256 }));
    }

    /// <summary>Everything a complete application needs, in the order the app syncs it. Does not submit.</summary>
    public static async Task CompleteAsync(HttpClient client)
    {
        await CreateAccountAsync(client);
        await Json.ExpectSuccessAsync(await client.PutAsJsonAsync("/api/v1/provider-accounts/me", Details()));

        var cleaning = await CategoryIdAsync(client, "cleaning");
        var offering = await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/provider-offerings", Offering(cleaning, null)));
        await Json.ExpectSuccessAsync(await client.PutAsJsonAsync(
            $"/api/v1/provider-offerings/{offering.GetProperty("id")}", Offering(cleaning, 80m)));

        await Json.ExpectSuccessAsync(await client.PostAsJsonAsync("/api/v1/provider-service-areas", new
        {
            serviceAreaId = await AreaIdAsync(client, "East Legon"),
            maximumTravelDistanceKm = 10.0, supportsEmergency = false, supportsSameDay = true,
        }));
        await Json.ExpectSuccessAsync(await client.PutAsJsonAsync("/api/v1/provider-availability/rules",
            Availability("Monday", "Tuesday", "Wednesday", "Thursday", "Friday")));

        await AddDocumentAsync(client, "GhanaCard");
        await AddDocumentAsync(client, "GhanaCardBack");
        await AddDocumentAsync(client, "Selfie");
        await Json.ExpectSuccessAsync(await client.PostAsync("/api/v1/provider-standards/current/accept", null));
    }
}
