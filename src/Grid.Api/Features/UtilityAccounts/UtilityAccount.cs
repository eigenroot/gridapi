using Grid.Api.Data;

namespace Grid.Api.Features.UtilityAccounts;

/// <summary>
/// A saved payee: an ECG meter, water account, TV smartcard or phone number. Saving one says
/// nothing about who owns it — people pay for relatives' meters.
/// </summary>
public sealed class UtilityAccount : Entity
{
    public Guid OwnerConsumerId { get; set; }
    public UtilityType Type { get; set; }
    public required string AccountNumber { get; set; }
    public required string Nickname { get; set; }
    public Guid? PropertyId { get; set; }

    /// <summary>The person's preferred account for this utility type; at most one per type.</summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }
}
