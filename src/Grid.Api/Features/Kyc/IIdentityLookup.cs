namespace Grid.Api.Features.Kyc;

public enum IdentityLookupOutcome
{
    /// <summary>The telco returned a registered name.</summary>
    Found,

    /// <summary>The telco has no name for this number; asking again won't help.</summary>
    NotFound,

    /// <summary>Network, timeout or provider trouble; worth retrying later.</summary>
    Retry,
}

public sealed record IdentityLookupResult(IdentityLookupOutcome Outcome, string? Name = null, string? Error = null);

/// <summary>Looks up the name a phone number's SIM is registered to.</summary>
public interface IIdentityLookup
{
    /// <summary>Short provider name stored as <c>Consumer.KycProvider</c>.</summary>
    string Provider { get; }

    bool Enabled { get; }

    Task<IdentityLookupResult> LookupNameAsync(string phoneNumber, CancellationToken ct);
}

/// <summary>KYC switched off.</summary>
public sealed class NoIdentityLookup : IIdentityLookup
{
    public string Provider => "none";
    public bool Enabled => false;

    public Task<IdentityLookupResult> LookupNameAsync(string phoneNumber, CancellationToken ct) =>
        Task.FromResult(new IdentityLookupResult(IdentityLookupOutcome.Retry, Error: "KYC is disabled."));
}
