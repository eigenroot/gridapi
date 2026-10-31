using Grid.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Providers;

public sealed record MissingRequirement(string Code, string Message);

public sealed record ReadinessResponse(bool IsReady, int CompletionPercentage, List<MissingRequirement> MissingRequirements);

/// <summary>
/// Whether an application can be submitted, judged only from what the server holds. Clients
/// render <c>missingRequirements</c> as given so what they show matches what submit accepts.
/// Payout is not required here; it gates going online, not verification.
/// </summary>
public static class ProviderReadiness
{
    private static readonly VerificationDocumentType[] ProfilePhotoTypes =
        [VerificationDocumentType.ProfilePhoto, VerificationDocumentType.Selfie];

    public static async Task<ReadinessResponse> EvaluateAsync(GridDbContext db, ProviderAccount account, CancellationToken ct)
    {
        var id = account.Id;
        var offerings = await db.ProviderOfferings.Where(o => o.ProviderAccountId == id && o.IsActive).ToListAsync(ct);
        var hasArea = await db.ProviderServiceAreas.AnyAsync(a => a.ProviderAccountId == id, ct);
        var hasAvailability = await db.ProviderAvailabilityRules.AnyAsync(r => r.ProviderAccountId == id && r.IsAvailable, ct);
        var documents = await db.VerificationDocuments
            .Where(d => d.ProviderAccountId == id && d.SupersededAt == null && d.ReviewStatus != ReviewStatus.Rejected)
            .Select(d => d.DocumentType)
            .ToListAsync(ct);
        var acceptedStandards = await db.StandardsAcceptances.AnyAsync(
            s => s.ProviderAccountId == id && s.StandardsVersion == ProviderStandards.CurrentVersion, ct);

        // The app collects a selfie, not a separate profile photo; either one satisfies it.
        (bool Met, string Code, string Message)[] checks =
        [
            (!string.IsNullOrWhiteSpace(account.DisplayName)
             && (account.DeliveryMode == DeliveryMode.TravelsToCustomer || !string.IsNullOrWhiteSpace(account.PremisesAddress)),
                "details.required", "Add your name and how you deliver your service."),
            (offerings.Count > 0, "offering.required", "Choose the service you offer."),
            (offerings.Count > 0 && offerings.All(o => o.HasRequiredPrice), "pricing.required", "Set your price."),
            (hasArea, "service_area.required", "Choose at least one area you work in."),
            (hasAvailability, "availability.required", "Choose the days you're available."),
            (documents.Contains(VerificationDocumentType.GhanaCard),
                "identity.ghana_card_required", "Add a photo of the front of your Ghana Card."),
            (documents.Contains(VerificationDocumentType.GhanaCardBack),
                "identity.ghana_card_back_required", "Add a photo of the back of your Ghana Card."),
            (documents.Any(ProfilePhotoTypes.Contains),
                "identity.profile_photo_required", "Add a selfie so customers can recognise you."),
            (acceptedStandards, "standards.required", "Accept the GRID professional standards."),
        ];

        var missing = checks.Where(c => !c.Met).Select(c => new MissingRequirement(c.Code, c.Message)).ToList();
        var percentage = (checks.Length - missing.Count) * 100 / checks.Length;
        return new ReadinessResponse(missing.Count == 0, percentage, missing);
    }
}
