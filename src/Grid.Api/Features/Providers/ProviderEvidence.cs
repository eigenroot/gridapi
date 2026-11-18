using Grid.Api.Data;

namespace Grid.Api.Features.Providers;

public enum VerificationDocumentType
{
    GhanaCard, GhanaCardBack, Selfie, ProfilePhoto, ProfessionalCertificate, BusinessRegistration,

    /// <summary>The EPA licence pest control operators must hold.</summary>
    EpaPestControlLicence,

    /// <summary>The Energy Commission's electrical wiring certificate.</summary>
    EnergyCommissionCertificate,
}

public enum ReviewStatus { Pending, Approved, Rejected }

/// <summary>
/// A private file GRID reviews to verify the provider. Adding a new one of a single-instance
/// type (a Ghana Card side, a selfie) supersedes the previous one rather than deleting it.
/// </summary>
public sealed class VerificationDocument : Entity
{
    public Guid ProviderAccountId { get; set; }
    public VerificationDocumentType DocumentType { get; set; }
    public Guid MediaObjectId { get; set; }
    public required string StorageKey { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }

    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public string? ReviewNote { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }

    public DateTimeOffset? SupersededAt { get; set; }

    /// <summary>Types a provider holds at most one current copy of; certificates can be many.</summary>
    public static bool IsSingleInstance(VerificationDocumentType type) =>
        type is not VerificationDocumentType.ProfessionalCertificate;

    /// <summary>Papers rather than photos of the person: uploaded as certificates.</summary>
    public static bool IsCertificate(VerificationDocumentType type) =>
        type is VerificationDocumentType.ProfessionalCertificate or VerificationDocumentType.BusinessRegistration
            or VerificationDocumentType.EpaPestControlLicence or VerificationDocumentType.EnergyCommissionCertificate;

    /// <summary>The licence a service needs before GRID can approve a provider for it, by category slug.</summary>
    public static readonly IReadOnlyDictionary<string, (VerificationDocumentType Type, string Code, string Message)> RequiredFor =
        new Dictionary<string, (VerificationDocumentType, string, string)>
        {
            ["pest-control"] = (VerificationDocumentType.EpaPestControlLicence, "licence.epa_required",
                "Add your EPA pest control licence."),
            ["electrical"] = (VerificationDocumentType.EnergyCommissionCertificate, "licence.energy_commission_required",
                "Add your Energy Commission wiring certificate."),
        };
}

/// <summary>An example of past work. Publishable later, so it is kept apart from verification documents.</summary>
public sealed class PortfolioItem : Entity
{
    public Guid ProviderAccountId { get; set; }
    public Guid? ServiceCategoryId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public Guid MediaObjectId { get; set; }
    public required string StorageKey { get; set; }
    public required string ContentType { get; set; }
    public DateOnly? ProjectCompletedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

/// <summary>Who accepted which version of the GRID professional standards, and when.</summary>
public sealed class StandardsAcceptance : Entity
{
    public Guid ProviderAccountId { get; set; }
    public required string StandardsVersion { get; set; }
    public Guid AcceptedByConsumerId { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
}

public static class ProviderStandards
{
    /// <summary>Bump when the standards text changes; providers then need to accept again.</summary>
    public const string CurrentVersion = "2026-10";
}
