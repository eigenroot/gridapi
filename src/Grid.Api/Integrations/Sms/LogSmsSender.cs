namespace Grid.Api.Integrations.Sms;

/// <summary>Development stand-in: writes the message to the log instead of sending it.</summary>
public sealed class LogSmsSender(ILogger<LogSmsSender> logger) : ISmsSender
{
    public SmsProviderKind Kind => SmsProviderKind.Log;

    public Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken ct)
    {
        logger.LogInformation("[SMS to {To}] {Body}", message.To, message.Body);
        return Task.FromResult(new SmsSendResult(true, Kind, $"log-{Guid.NewGuid():N}"));
    }
}
