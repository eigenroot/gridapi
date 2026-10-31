using Grid.Api.Common;
using Grid.Api.Data;

namespace Grid.Api.Features.Consumers;

public enum ConsumerStatus { Active, Suspended }

public enum KycStatus { NotChecked, Pending, Verified, NotFound, Failed }

/// <summary>
/// One person, identified by their phone number, shared by GRID Home and GRID Pro.
/// Self-provided names and the telco-registered (KYC) name are kept apart and never overwrite
/// each other.
/// </summary>
public sealed class Consumer : Entity
{
    /// <summary><c>233XXXXXXXXX</c>; unique.</summary>
    public required string PhoneNumber { get; set; }
    public PhoneNetwork PhoneNetwork { get; set; }

    // What the person told us.
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset? ProfileCompletedAt { get; set; }

    // What the telco told us.
    public string? KycName { get; set; }
    public KycStatus KycStatus { get; set; } = KycStatus.NotChecked;
    public string? KycProvider { get; set; }
    public DateTimeOffset? KycCheckedAt { get; set; }

    // Background lookup retry state.
    public int KycAttempts { get; set; }
    public DateTimeOffset? KycNextAttemptAt { get; set; }
    public string? KycLastError { get; set; }

    public Guid? DefaultPropertyId { get; set; }
    public ConsumerStatus Status { get; set; } = ConsumerStatus.Active;

    public AppKind FirstSeenApp { get; set; }
    public AppKind LastLoginApp { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public string FullName => string.Join(' ', new[] { FirstName, LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));

    public bool ProfileComplete => ProfileCompletedAt is not null;
}
