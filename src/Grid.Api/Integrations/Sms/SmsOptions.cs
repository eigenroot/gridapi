using Microsoft.Extensions.Options;

namespace Grid.Api.Integrations.Sms;

public enum SmsProviderKind { Log, Arkesel, Hubtel }

/// <summary>
/// <c>Sms:Provider</c> picks who delivers texts; <c>Sms:FallbackProvider</c> (optional) is tried
/// once when the primary fails. Both can be set from env, e.g. <c>Sms__Provider=Hubtel</c>.
/// </summary>
public sealed class SmsOptions
{
    public const string Section = "Sms";

    public SmsProviderKind Provider { get; set; } = SmsProviderKind.Log;
    public SmsProviderKind? FallbackProvider { get; set; }

    /// <summary>Must be approved on every provider account in use; 11 characters at most.</summary>
    public string SenderId { get; set; } = "GRID";

    public ArkeselSmsOptions Arkesel { get; set; } = new();
    public HubtelSmsOptions Hubtel { get; set; } = new();
}

public sealed class ArkeselSmsOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://sms.arkesel.com";

    /// <summary>Arkesel accepts and bills nothing for sandboxed sends; nothing reaches a phone.</summary>
    public bool Sandbox { get; set; }
}

public sealed class HubtelSmsOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string BaseUrl { get; set; } = "https://sms.hubtel.com";
}

public sealed class SmsOptionsValidator : IValidateOptions<SmsOptions>
{
    public ValidateOptionsResult Validate(string? name, SmsOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SenderId) || options.SenderId.Length > 11)
            failures.Add("Sms:SenderId is required and must be 11 characters or fewer.");

        if (options.FallbackProvider == options.Provider)
            failures.Add("Sms:FallbackProvider must differ from Sms:Provider.");

        foreach (var provider in new[] { options.Provider, options.FallbackProvider })
        {
            switch (provider)
            {
                case SmsProviderKind.Arkesel when string.IsNullOrWhiteSpace(options.Arkesel.ApiKey):
                    failures.Add("Sms:Arkesel:ApiKey is required when Arkesel is in use.");
                    break;
                case SmsProviderKind.Hubtel when string.IsNullOrWhiteSpace(options.Hubtel.ClientId)
                                                 || string.IsNullOrWhiteSpace(options.Hubtel.ClientSecret):
                    failures.Add("Sms:Hubtel:ClientId and ClientSecret are required when Hubtel is in use.");
                    break;
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
