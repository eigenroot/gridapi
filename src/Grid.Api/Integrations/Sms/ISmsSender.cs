namespace Grid.Api.Integrations.Sms;

public sealed record SmsMessage(string To, string Body, string Purpose);

/// <summary>
/// The outcome of handing a message to a provider. <see cref="Accepted"/> means the provider
/// took it for delivery, not that it reached the phone.
/// </summary>
public sealed record SmsSendResult(
    bool Accepted,
    SmsProviderKind Provider,
    string? ProviderMessageId = null,
    decimal? Cost = null,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static SmsSendResult Failed(SmsProviderKind provider, string code, string? message) =>
        new(false, provider, ErrorCode: code, ErrorMessage: message);
}

/// <summary>
/// One SMS provider. Implementations never throw for provider or network failures; they return
/// a failed result so the router can fall back.
/// </summary>
public interface ISmsSender
{
    SmsProviderKind Kind { get; }

    Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken ct);
}
