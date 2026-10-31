using Grid.Api.Data;

namespace Grid.Api.Features.Providers;

public enum ProviderAccountType { Individual, Business }

public enum DeliveryMode { TravelsToCustomer, FixedPremises, Both }

public enum ApplicationStatus { Draft, Submitted, UnderReview, MoreInformationRequired, Approved, Rejected, Withdrawn }

/// <summary>The application's status mirrored onto the account, plus <c>Suspended</c>, which only an account can be.</summary>
public enum ProviderVerificationStatus { Draft, Submitted, UnderReview, MoreInformationRequired, Approved, Rejected, Withdrawn, Suspended }

public enum OperatingStatus { Offline, Online }

/// <summary>
/// The individual or business that delivers services. Owned by one consumer (one account per
/// consumer in v1); jobs, reputation and earnings will hang off it later.
/// </summary>
public sealed class ProviderAccount : Entity
{
    public Guid OwnerConsumerId { get; set; }
    public ProviderAccountType AccountType { get; set; }
    public required string DisplayName { get; set; }
    public string? LegalName { get; set; }
    public string? Description { get; set; }
    public string? Email { get; set; }
    public string? BusinessRegistrationNumber { get; set; }
    public string? City { get; set; }
    public string? PrimaryArea { get; set; }
    public int YearsOfExperience { get; set; }
    public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.TravelsToCustomer;
    public string? PremisesAddress { get; set; }

    public ProviderVerificationStatus VerificationStatus { get; set; } = ProviderVerificationStatus.Draft;
    public OperatingStatus OperatingStatus { get; set; } = OperatingStatus.Offline;
}

/// <summary>
/// The account's application to be verified. One per account; <see cref="Version"/> counts
/// submissions, and each submission keeps its own snapshot in <see cref="ProviderApplicationSubmission"/>.
/// </summary>
public sealed class ProviderApplication : Entity
{
    public Guid ProviderAccountId { get; set; }
    public int Version { get; set; } = 1;
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Draft;

    /// <summary>Where the provider is in the app's onboarding flow, for resuming on another device.</summary>
    public int CurrentStep { get; set; }

    // Experience, as the provider describes it.
    public string? MainSkill { get; set; }
    public string? SecondarySkills { get; set; }
    public string? ToolsNotes { get; set; }
    public string? Certifications { get; set; }
    public string? ExperienceDescription { get; set; }
    public int? YearsOperating { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public string? RejectionReason { get; set; }

    /// <summary>Verification-critical details can't change while GRID is reviewing or has approved them.</summary>
    public bool IsLocked => Status is ApplicationStatus.Submitted or ApplicationStatus.UnderReview or ApplicationStatus.Approved;
}

/// <summary>What was submitted, frozen at the moment of submission, so a review judges a fixed record.</summary>
public sealed class ProviderApplicationSubmission : Entity
{
    public Guid ProviderApplicationId { get; set; }
    public int Version { get; set; }

    /// <summary>JSON (jsonb) of the account, offerings, areas, availability, documents and standards version.</summary>
    public required string Snapshot { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}
