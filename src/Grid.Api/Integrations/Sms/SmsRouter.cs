using Grid.Api.Common;
using Microsoft.Extensions.Options;

namespace Grid.Api.Integrations.Sms;

/// <summary>Sends through the configured provider, then once through the fallback if that fails.</summary>
public sealed class SmsRouter(
    IEnumerable<ISmsSender> senders,
    IOptions<SmsOptions> options,
    ILogger<SmsRouter> logger)
{
    private readonly Dictionary<SmsProviderKind, ISmsSender> _senders = senders.ToDictionary(s => s.Kind);

    public async Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken ct)
    {
        var config = options.Value;
        var result = await SendWithAsync(config.Provider, message, ct);
        if (result.Accepted || config.FallbackProvider is not { } fallback) return result;

        return await SendWithAsync(fallback, message, ct);
    }

    private async Task<SmsSendResult> SendWithAsync(SmsProviderKind provider, SmsMessage message, CancellationToken ct)
    {
        if (!_senders.TryGetValue(provider, out var sender))
        {
            logger.LogError("No SMS sender is registered for {Provider}", provider);
            return SmsSendResult.Failed(provider, "not_configured", "Provider not registered.");
        }

        var result = await sender.SendAsync(message, ct);
        if (result.Accepted)
        {
            logger.LogInformation(
                "SMS {Purpose} to {To} accepted by {Provider} as {ProviderMessageId} (cost {Cost})",
                message.Purpose, PhoneNumbers.Mask(message.To), provider, result.ProviderMessageId, result.Cost);
        }
        else
        {
            logger.LogWarning(
                "SMS {Purpose} to {To} rejected by {Provider}: {ErrorCode} {ErrorMessage}",
                message.Purpose, PhoneNumbers.Mask(message.To), provider, result.ErrorCode, result.ErrorMessage);
        }
        return result;
    }
}
