using System.Net.Http.Json;
using System.Text.Json;
using Grid.Api.Features.Providers;
using Grid.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Tests;

public class ProviderOnboardingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> ReadinessAsync(HttpClient client) =>
        await Json.ExpectSuccessAsync(await client.GetAsync("/api/v1/provider-applications/me/readiness"));

    private static HashSet<string?> MissingCodes(JsonElement readiness) =>
        readiness.GetProperty("missingRequirements").EnumerateArray().Select(r => r.GetProperty("code").GetString()).ToHashSet();

    [Fact]
    public async Task Catalogue_is_public_and_holds_cleaning_and_laundry()
    {
        var anonymous = api.Client("pro");
        var categories = await Onboarding.CatalogueAsync(anonymous, "service-categories");
        var parents = categories.EnumerateArray().Where(c => c.GetProperty("parentCategoryId").ValueKind == JsonValueKind.Null).ToList();
        Assert.Equal(["cleaning", "laundry"], parents.Select(c => c.GetProperty("slug").GetString()));

        var cleaningId = parents[0].GetProperty("id").GetString();
        var children = categories.EnumerateArray().Where(c => c.GetProperty("parentCategoryId").GetString() == cleaningId).ToList();
        Assert.Equal(6, children.Count);
        Assert.Contains(children, c => c.GetProperty("name").GetString() == "Deep cleaning");
        Assert.Equal("PerUnit", parents[0].GetProperty("defaultPricingMode").GetString());

        var areas = await Onboarding.CatalogueAsync(anonymous, "service-areas");
        Assert.Equal(50, areas.GetArrayLength());
        Assert.Contains(areas.EnumerateArray(), a =>
            a.GetProperty("name").GetString() == "East Legon" && a.GetProperty("region").GetString() == "Greater Accra");
    }

    [Fact]
    public async Task A_provider_completes_onboarding_and_submits()
    {
        var me = await api.SignInAsync("0551000001", "pro", fullName: "Akosua Boateng");
        Assert.Equal(JsonValueKind.Null, me.User.GetProperty("providerAccountId").ValueKind);

        // The app asks for the account first and creates it on "not found".
        await Json.ExpectErrorAsync(await me.Client.GetAsync("/api/v1/provider-accounts/me"), 404, "providers.account_not_found");
        var account = await Onboarding.CreateAccountAsync(me.Client);
        Assert.Equal("Draft", account.GetProperty("verificationStatus").GetString());

        var user = await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/auth/me"));
        Assert.Equal(account.GetProperty("id").GetGuid(), user.GetProperty("providerAccountId").GetGuid());

        await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-accounts/me", Onboarding.Details()));

        // The services step saves the offering before it has a price; that is accepted, and readiness says so.
        var cleaning = await Onboarding.CategoryIdAsync(me.Client, "cleaning");
        var offering = await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-offerings", Onboarding.Offering(cleaning, null)));
        Assert.Contains("pricing.required", MissingCodes(await ReadinessAsync(me.Client)));

        // A resumed session finds the offering by category and updates it.
        var offerings = await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/provider-offerings"));
        Assert.Equal(cleaning, Assert.Single(offerings.EnumerateArray()).GetProperty("serviceCategoryId").GetGuid());
        var priced = await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync(
            $"/api/v1/provider-offerings/{offering.GetProperty("id")}", Onboarding.Offering(cleaning, 80m)));
        Assert.Equal(80m, priced.GetProperty("unitPrice").GetDecimal());

        await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-applications/me/experience", new
        {
            mainSkill = "cleaning", secondarySkills = "Deep cleaning", toolsNotes = "Own supplies",
            certifications = (string?)null, experienceDescription = "Four years of home cleaning.", yearsOperating = 4,
        }));

        var eastLegon = await Onboarding.AreaIdAsync(me.Client, "East Legon");
        await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-service-areas",
            new { serviceAreaId = eastLegon, maximumTravelDistanceKm = 10.0, supportsEmergency = false, supportsSameDay = true }));
        var rules = await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-availability/rules",
            Onboarding.Availability("Monday", "Wednesday", "Friday")));
        Assert.Equal(7, rules.GetArrayLength());
        Assert.Equal("Monday", rules[0].GetProperty("dayOfWeek").GetString());
        Assert.Equal("08:00:00", rules[0].GetProperty("startTime").GetString());

        await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/payout-methods", new
        {
            methodType = "MobileMoney", providerName = "MTN Mobile Money", accountName = "Akosua Boateng",
            accountNumber = "0551000001", makeDefault = true,
        }));

        var workPhoto = await Onboarding.UploadAsync(me.Client, "portfolio", Onboarding.Jpeg());
        await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-portfolio", new
        {
            serviceCategoryId = (Guid?)null, title = "Completed work", description = (string?)null,
            storageKey = workPhoto, contentType = "image/jpeg", projectCompletedAt = (string?)null,
        }));
        await Onboarding.AddDocumentAsync(me.Client, "ProfessionalCertificate", "certificates");

        await Onboarding.AddDocumentAsync(me.Client, "GhanaCard");
        await Onboarding.AddDocumentAsync(me.Client, "GhanaCardBack");
        await Onboarding.AddDocumentAsync(me.Client, "Selfie");
        await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-applications/me/current-step", new { step = 10 }));

        // Only the standards remain, and the selfie counts as the profile photo.
        var readiness = await ReadinessAsync(me.Client);
        Assert.Equal(new HashSet<string?> { "standards.required" }, MissingCodes(readiness));
        Assert.Equal(88, readiness.GetProperty("completionPercentage").GetInt32());

        await Json.ExpectSuccessAsync(await me.Client.PostAsync("/api/v1/provider-standards/current/accept", null));
        readiness = await ReadinessAsync(me.Client);
        Assert.True(readiness.GetProperty("isReady").GetBoolean());
        Assert.Equal(100, readiness.GetProperty("completionPercentage").GetInt32());

        var submitted = await Json.ExpectSuccessAsync(await me.Client.PostAsync("/api/v1/provider-applications/me/submit", null));
        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());
        Assert.Equal(1, submitted.GetProperty("version").GetInt32());
        Assert.Equal(10, submitted.GetProperty("currentStep").GetInt32());
        Assert.Equal("Four years of home cleaning.", submitted.GetProperty("experienceDescription").GetString());

        account = await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/provider-accounts/me"));
        Assert.Equal("Submitted", account.GetProperty("verificationStatus").GetString());

        var documents = await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/provider-verification-documents"));
        Assert.Equal(4, documents.GetArrayLength());
        Assert.All(documents.EnumerateArray(), d => Assert.Equal("Pending", d.GetProperty("reviewStatus").GetString()));

        var applicationId = submitted.GetProperty("id").GetGuid();
        var snapshot = await api.WithDbAsync(db => db.ProviderApplicationSubmissions.SingleAsync(s => s.ProviderApplicationId == applicationId));
        using var json = JsonDocument.Parse(snapshot.Snapshot);
        Assert.Equal("Akosua Boateng", json.RootElement.GetProperty("account").GetProperty("displayName").GetString());
        Assert.Equal(4, json.RootElement.GetProperty("documents").GetArrayLength());

        // A refreshed session reports the provider account too.
        var refreshed = await Json.ExpectSuccessAsync(await api.Client("pro").PostAsJsonAsync(
            "/api/v1/auth/tokens/refresh", new { refreshToken = me.RefreshToken }));
        Assert.Equal(account.GetProperty("id").GetGuid(), refreshed.GetProperty("user").GetProperty("providerAccountId").GetGuid());
    }

    [Fact]
    public async Task Provider_endpoints_need_an_account_and_allow_only_one()
    {
        var me = await api.SignInAsync("0551000002", "pro", fullName: "Kofi Asante");
        foreach (var path in new[]
                 {
                     "provider-applications/me", "provider-applications/me/readiness", "provider-offerings",
                     "provider-service-areas", "provider-availability/rules", "provider-portfolio",
                     "provider-verification-documents", "provider-standards/current", "payout-methods",
                 })
        {
            await Json.ExpectErrorAsync(await me.Client.GetAsync($"/api/v1/{path}"), 404, "providers.account_not_found");
        }

        await Onboarding.CreateAccountAsync(me.Client, "Kofi Asante");
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-accounts",
            new { accountType = "Business", displayName = "Kofi Cleans" }), 409, "providers.account_exists");

        var error = await Json.ExpectErrorAsync(await api.Client("pro").GetAsync("/api/v1/provider-accounts/me"), 401, "auth.unauthenticated");
        Assert.NotNull(error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Submitting_an_incomplete_application_lists_what_is_missing()
    {
        var me = await api.SignInAsync("0551000003", "pro", fullName: "Esi Mensah");
        await Onboarding.CreateAccountAsync(me.Client, "Esi Mensah");

        var readiness = await ReadinessAsync(me.Client);
        Assert.False(readiness.GetProperty("isReady").GetBoolean());
        Assert.Equal(11, readiness.GetProperty("completionPercentage").GetInt32()); // only details (name, travels) is met

        var error = await Json.ExpectErrorAsync(await me.Client.PostAsync("/api/v1/provider-applications/me/submit", null),
            422, "providers.application_incomplete");
        var codes = error.GetProperty("detail").GetProperty("missingRequirements").EnumerateArray()
            .Select(r => r.GetProperty("code").GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?>
        {
            "offering.required", "pricing.required", "service_area.required", "availability.required",
            "identity.ghana_card_required", "identity.ghana_card_back_required", "identity.profile_photo_required",
            "standards.required",
        }, codes);
    }

    [Fact]
    public async Task Submitted_applications_lock_until_more_information_is_requested()
    {
        var me = await api.SignInAsync("0551000004", "pro", fullName: "Yaw Owusu");
        await Onboarding.CompleteAsync(me.Client);
        await Json.ExpectSuccessAsync(await me.Client.PostAsync("/api/v1/provider-applications/me/submit", null));

        // A retried submit is harmless.
        var again = await Json.ExpectSuccessAsync(await me.Client.PostAsync("/api/v1/provider-applications/me/submit", null));
        Assert.Equal(1, again.GetProperty("version").GetInt32());

        var cleaning = await Onboarding.CategoryIdAsync(me.Client, "cleaning");
        var laundry = await Onboarding.CategoryIdAsync(me.Client, "laundry");
        await Json.ExpectErrorAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-accounts/me", Onboarding.Details("Someone Else")),
            409, "providers.locked");
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-offerings", Onboarding.Offering(laundry, 10m)),
            409, "providers.locked");
        await Json.ExpectErrorAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-applications/me/experience", new { yearsOperating = 1 }),
            409, "providers.locked");
        var photo = await Onboarding.UploadAsync(me.Client, "identity", Onboarding.Jpeg());
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-verification-documents",
            new { documentType = "GhanaCard", storageKey = photo }), 409, "providers.locked");

        // Progress and working preferences are not locked.
        await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-applications/me/current-step", new { step = 3 }));
        await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-availability/rules", Onboarding.Availability("Saturday")));

        // GRID asks for corrections (Ops endpoints don't exist yet), which unlocks editing.
        var accountId = (await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/provider-accounts/me"))).GetProperty("id").GetGuid();
        await api.WithDbAsync(async db =>
        {
            var application = await db.ProviderApplications.SingleAsync(a => a.ProviderAccountId == accountId);
            application.Status = ApplicationStatus.MoreInformationRequired;
            return await db.SaveChangesAsync();
        });

        await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-accounts/me", Onboarding.Details("Yaw Owusu")));
        await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-verification-documents",
            new { documentType = "GhanaCard", storageKey = photo }));
        Assert.Equal(cleaning, (await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/provider-offerings")))[0]
            .GetProperty("serviceCategoryId").GetGuid());

        var resubmitted = await Json.ExpectSuccessAsync(await me.Client.PostAsync("/api/v1/provider-applications/me/submit", null));
        Assert.Equal("Submitted", resubmitted.GetProperty("status").GetString());
        Assert.Equal(2, resubmitted.GetProperty("version").GetInt32());

        var versions = await api.WithDbAsync(db => db.ProviderApplicationSubmissions
            .Where(s => s.ProviderApplicationId == resubmitted.GetProperty("id").GetGuid())
            .OrderBy(s => s.Version).Select(s => s.Version).ToListAsync());
        Assert.Equal([1, 2], versions);

        // The new Ghana Card replaced the old one; history is kept.
        var cards = await api.WithDbAsync(db => db.VerificationDocuments
            .Where(d => d.ProviderAccountId == accountId && d.DocumentType == VerificationDocumentType.GhanaCard)
            .ToListAsync());
        Assert.Equal(2, cards.Count);
        Assert.Equal(photo, Assert.Single(cards, c => c.SupersededAt == null).StorageKey);
    }

    [Fact]
    public async Task Details_need_a_premises_address_for_customers_who_visit()
    {
        var me = await api.SignInAsync("0551000005", "pro", fullName: "Ama Laundry");
        await Onboarding.CreateAccountAsync(me.Client, "Ama Laundry");

        var error = await Json.ExpectErrorAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-accounts/me",
            Onboarding.Details("Ama Laundry", "Both")), 400, "providers.premises_address_required");
        Assert.Equal("premisesAddress", error.GetProperty("fields")[0].GetProperty("field").GetString());

        var account = await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-accounts/me",
            Onboarding.Details("Ama Laundry", "Both", "Shop 4, Adenta Market")));
        Assert.Equal("Both", account.GetProperty("deliveryMode").GetString());

        await Json.ExpectErrorAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-accounts/me", new { displayName = "" }),
            400, "validation.failed");
    }

    [Fact]
    public async Task Offerings_are_one_per_top_level_service()
    {
        var me = await api.SignInAsync("0551000006", "pro", fullName: "Kwesi Clean");
        await Onboarding.CreateAccountAsync(me.Client, "Kwesi Clean");
        var cleaning = await Onboarding.CategoryIdAsync(me.Client, "cleaning");
        var deepCleaning = await Onboarding.CategoryIdAsync(me.Client, "cleaning-deep-cleaning");

        await Json.ExpectSuccessAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-offerings", Onboarding.Offering(cleaning, 50m)));
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-offerings", Onboarding.Offering(cleaning, 60m)),
            409, "providers.offering_exists");
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-offerings", Onboarding.Offering(deepCleaning, 60m)),
            404, "catalogue.category_not_found");
        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-offerings", Onboarding.Offering(cleaning, -1m)),
            400, "validation.failed");
    }

    [Fact]
    public async Task Service_areas_are_idempotent_and_removable()
    {
        var me = await api.SignInAsync("0551000007", "pro", fullName: "Abena Areas");
        await Onboarding.CreateAccountAsync(me.Client, "Abena Areas");
        var osu = await Onboarding.AreaIdAsync(me.Client, "Osu");

        var first = await me.Client.PostAsJsonAsync("/api/v1/provider-service-areas", new { serviceAreaId = osu, maximumTravelDistanceKm = 5 });
        Assert.Equal(201, (int)first.StatusCode);
        var again = await me.Client.PostAsJsonAsync("/api/v1/provider-service-areas", new { serviceAreaId = osu, maximumTravelDistanceKm = 8 });
        Assert.Equal(200, (int)again.StatusCode);

        var areas = await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/provider-service-areas"));
        var area = Assert.Single(areas.EnumerateArray());
        Assert.Equal(osu, area.GetProperty("serviceAreaId").GetGuid());
        Assert.Equal("Osu", area.GetProperty("name").GetString());
        Assert.Equal(8m, area.GetProperty("maximumTravelDistanceKm").GetDecimal());

        await Json.ExpectErrorAsync(await me.Client.PostAsJsonAsync("/api/v1/provider-service-areas", new { serviceAreaId = Guid.NewGuid() }),
            404, "catalogue.area_not_found");

        var other = await api.SignInAsync("0551000008", "pro", fullName: "Someone Else");
        await Onboarding.CreateAccountAsync(other.Client, "Someone Else");
        await Json.ExpectErrorAsync(await other.Client.DeleteAsync($"/api/v1/provider-service-areas/{area.GetProperty("id")}"),
            404, "providers.service_area_not_found");

        await Json.ExpectSuccessAsync(await me.Client.DeleteAsync($"/api/v1/provider-service-areas/{area.GetProperty("id")}"));
        Assert.Empty((await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/provider-service-areas"))).EnumerateArray());
    }

    [Fact]
    public async Task Availability_is_replaced_whole_and_validated()
    {
        var me = await api.SignInAsync("0551000009", "pro", fullName: "Kojo Hours");
        await Onboarding.CreateAccountAsync(me.Client, "Kojo Hours");

        await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-availability/rules", Onboarding.Availability("Monday")));
        var rules = await Json.ExpectSuccessAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-availability/rules", new
        {
            rules = new[] { new { dayOfWeek = "Saturday", startTime = "09:00:00", endTime = "13:00:00", isAvailable = true } },
        }));
        var only = Assert.Single(rules.EnumerateArray());
        Assert.Equal("Saturday", only.GetProperty("dayOfWeek").GetString());

        var error = await Json.ExpectErrorAsync(await me.Client.PutAsJsonAsync("/api/v1/provider-availability/rules", new
        {
            rules = new[]
            {
                new { dayOfWeek = "Monday", startTime = "17:00:00", endTime = "08:00:00", isAvailable = true },
                new { dayOfWeek = "Monday", startTime = "08:00:00", endTime = "17:00:00", isAvailable = true },
            },
        }), 400, "validation.failed");
        var fields = error.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("field").GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?> { "rules[0].endTime", "rules[1].dayOfWeek" }, fields);
    }

    [Fact]
    public async Task Standards_acceptance_is_recorded_once_with_who_and_when()
    {
        var me = await api.SignInAsync("0551000010", "pro", fullName: "Adwoa Rules");
        await Onboarding.CreateAccountAsync(me.Client, "Adwoa Rules");

        var before = await Json.ExpectSuccessAsync(await me.Client.GetAsync("/api/v1/provider-standards/current"));
        Assert.False(before.GetProperty("accepted").GetBoolean());

        var first = await Json.ExpectSuccessAsync(await me.Client.PostAsync("/api/v1/provider-standards/current/accept", null));
        var second = await Json.ExpectSuccessAsync(await me.Client.PostAsync("/api/v1/provider-standards/current/accept", null));
        Assert.True(first.GetProperty("accepted").GetBoolean());
        Assert.Equal(first.GetProperty("acceptedAt").GetString(), second.GetProperty("acceptedAt").GetString());

        var consumerId = me.User.GetProperty("id").GetGuid();
        var row = await api.WithDbAsync(db => db.StandardsAcceptances.SingleAsync(s => s.AcceptedByConsumerId == consumerId));
        Assert.Equal(ProviderStandards.CurrentVersion, row.StandardsVersion);
    }
}
