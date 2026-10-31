using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Kyc;

public enum KycProviderKind { None, Hubtel }

/// <summary>
/// Telco name lookup. <c>Kyc:Provider=None</c> switches it off completely; sign-in never depends
/// on it. Turning it on later makes the worker look up everyone who signed up meanwhile.
/// </summary>
public sealed class KycOptions
{
    public const string Section = "Kyc";

    public KycProviderKind Provider { get; set; } = KycProviderKind.None;

    /// <summary>Whether this process runs the background worker (tests drive it directly).</summary>
    public bool RunWorker { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 5;

    public HubtelKycOptions Hubtel { get; set; } = new();
}

public sealed class HubtelKycOptions
{
    /// <summary>Hubtel Programmable API key id/secret, sent as Basic auth.</summary>
    public string ApiId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string CollectionAccountNumber { get; set; } = "";
    public string BaseUrl { get; set; } = "https://cs.hubtel.com";

    /// <summary>The MSISDN-name service id from Hubtel's Verification docs.</summary>
    public string MsisdnServiceId { get; set; } = "3e0841e70afc42fb97d13d19abd36384";

    public int TimeoutSeconds { get; set; } = 10;
}

public sealed class KycOptionsValidator : IValidateOptions<KycOptions>
{
    public ValidateOptionsResult Validate(string? name, KycOptions o)
    {
        if (o.Provider != KycProviderKind.Hubtel) return ValidateOptionsResult.Success;

        var h = o.Hubtel;
        return string.IsNullOrWhiteSpace(h.ApiId) || string.IsNullOrWhiteSpace(h.ApiKey)
                                                  || string.IsNullOrWhiteSpace(h.CollectionAccountNumber)
            ? ValidateOptionsResult.Fail("Kyc:Hubtel:ApiId, ApiKey and CollectionAccountNumber are required when Kyc:Provider is Hubtel.")
            : ValidateOptionsResult.Success;
    }
}
