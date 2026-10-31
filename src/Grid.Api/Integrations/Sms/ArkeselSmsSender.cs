using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Grid.Api.Integrations.Sms;

/// <summary>
/// Arkesel SMS API v2: <c>POST /api/v2/sms/send</c> with an <c>api-key</c> header.
/// See https://developers.arkesel.com (spec v2.4.0).
/// </summary>
public sealed class ArkeselSmsSender(HttpClient http, IOptions<SmsOptions> options, ILogger<ArkeselSmsSender> logger)
    : ISmsSender
{
    public SmsProviderKind Kind => SmsProviderKind.Arkesel;

    public async Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken ct)
    {
        var config = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(config.Arkesel.BaseUrl), "/api/v2/sms/send"));
        request.Headers.Add("api-key", config.Arkesel.ApiKey);
        request.Content = JsonContent.Create(new SendRequest(
            config.SenderId,
            message.Body,
            [message.To],
            config.Arkesel.Sandbox ? true : null));

        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return Interpret(response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Arkesel SMS request failed");
            return SmsSendResult.Failed(Kind, "network_error", ex.Message);
        }
    }

    internal SmsSendResult Interpret(HttpStatusCode status, string body)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException)
        {
            return SmsSendResult.Failed(Kind, $"http_{(int)status}", Truncate(body));
        }

        var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;

        if (status == HttpStatusCode.OK
            && root.TryGetProperty("status", out var s) && s.GetString() == "success"
            && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            // data holds one { recipient, id } per accepted number, plus { "invalid numbers": [...] }
            // when some were rejected.
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id))
                    return new SmsSendResult(true, Kind, id.GetString());
            }
            return SmsSendResult.Failed(Kind, "invalid_number", "Arkesel rejected the recipient number.");
        }

        var code = status switch
        {
            HttpStatusCode.PaymentRequired => "insufficient_balance",
            HttpStatusCode.Forbidden => "gateway_inactive",
            HttpStatusCode.UnprocessableEntity => "validation",
            HttpStatusCode.Unauthorized => "unauthorized",
            _ => $"http_{(int)status}",
        };
        return SmsSendResult.Failed(Kind, code, message ?? Truncate(body));
    }

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];

    private sealed record SendRequest(
        [property: JsonPropertyName("sender")] string Sender,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("recipients")] string[] Recipients,
        [property: JsonPropertyName("sandbox"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Sandbox);
}
