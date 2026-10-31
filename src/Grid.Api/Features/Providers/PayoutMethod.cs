using Grid.Api.Data;

namespace Grid.Api.Features.Providers;

public enum PayoutMethodType { MobileMoney, BankAccount }

public enum PayoutVerificationStatus { Unverified, Verified, Failed }

/// <summary>
/// Where a provider's earnings will be paid. The full account number is stored encrypted and
/// never returned; a keyed fingerprint lets the same destination be recognised without decrypting.
/// Nothing is paid out in the onboarding release.
/// </summary>
public sealed class PayoutMethod : Entity
{
    public Guid ProviderAccountId { get; set; }
    public PayoutMethodType MethodType { get; set; }
    public required string ProviderName { get; set; }
    public required string AccountName { get; set; }
    public required byte[] AccountNumberEncrypted { get; set; }
    public required string AccountNumberFingerprint { get; set; }
    public required string MaskedAccountNumber { get; set; }
    public PayoutVerificationStatus VerificationStatus { get; set; } = PayoutVerificationStatus.Unverified;
    public bool IsDefault { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}
