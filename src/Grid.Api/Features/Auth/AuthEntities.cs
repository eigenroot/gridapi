using Grid.Api.Common;
using Grid.Api.Data;

namespace Grid.Api.Features.Auth;

public enum OtpPurpose { Login }

/// <summary>One code we texted. The code itself is never stored, only a keyed hash of it.</summary>
public sealed class OtpChallenge : Entity
{
    public required string PhoneNumber { get; set; }
    public OtpPurpose Purpose { get; set; } = OtpPurpose.Login;
    public required string CodeHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>Set when a newer code is requested for the same phone.</summary>
    public DateTimeOffset? SupersededAt { get; set; }

    public AppKind App { get; set; }
    public string? RequestIp { get; set; }
    public string? SmsProvider { get; set; }
    public string? SmsProviderMessageId { get; set; }
}

/// <summary>
/// A single-use refresh token. Rotating it revokes this row and points at its replacement;
/// presenting an already-rotated token revokes the whole family (the whole sign-in session).
/// </summary>
public sealed class RefreshToken : Entity
{
    public Guid ConsumerId { get; set; }
    public required string TokenHash { get; set; }
    public Guid FamilyId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
    public AppKind App { get; set; }
    public string? UserAgent { get; set; }
    public string? CreatedIp { get; set; }
}
