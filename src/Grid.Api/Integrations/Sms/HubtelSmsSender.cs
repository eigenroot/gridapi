using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Grid.Api.Integrations.Sms;

/// <summary>
/// Hubtel SMS: <c>POST /v1/messages/send</c> with Basic auth (client id : client secret).
/// A <c>status</c> of 0 in the body means the message was accepted.
/// </summary>
public sealed class HubtelSmsSender(HttpClient http, IOptions<SmsOptions> options, ILogger<HubtelSmsSender> logger)
    : ISmsSender
{
    public SmsProviderKind Kind => SmsProviderKind.Hubtel;

    public async Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken ct)
    {
        var config = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(config.Hubtel.BaseUrl), "/v1/messages/send"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Hubtel.ClientId}:{config.Hubtel.ClientSecret}")));
        request.Content = JsonContent.Create(new SendRequest(config.SenderId, message.To, message.Body));

        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return Interpret(response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Hubtel SMS request failed");
            return SmsSendResult.Failed(Kind, "network_error", ex.Message);
        }
    }

    internal SmsSendResult Interpret(HttpStatusCode status, string body)
    {
        JsonElement root = default;
        var parsed = false;
        try
        {
            root = JsonDocument.Parse(body).RootElement;
            parsed = root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
        }

        if (parsed && (int)status is >= 200 and < 300
            && TryGetInt(root, "status", out var code) && code == 0)
        {
            return new SmsSendResult(
                true,
                Kind,
                GetString(root, "messageId"),
                TryGetDecimal(root, "rate"));
        }

        var description = parsed ? GetString(root, "statusDescription") ?? GetString(root, "message") : null;
        string errorCode;
        if (parsed && TryGetInt(root, "status", out var statusCode))
            errorCode = $"hubtel_{statusCode}";
        else
            errorCode = status switch
            {
                HttpStatusCode.Unauthorized => "unauthorized",
                HttpStatusCode.PaymentRequired => "insufficient_balance",
                HttpStatusCode.Forbidden => "forbidden",
                _ => $"http_{(int)status}",
            };
        return SmsSendResult.Failed(Kind, errorCode, description ?? (body.Length <= 300 ? body : body[..300]));
    }

    // Hubtel documents PascalCase field names; HttpClient's JSON defaults would send camelCase.
    private sealed record SendRequest(
        [property: JsonPropertyName("From")] string From,
        [property: JsonPropertyName("To")] string To,
        [property: JsonPropertyName("Content")] string Content);

    private static string? GetString(JsonElement root, string name) =>
        Find(root, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return Find(root, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out value);
    }

    private static decimal? TryGetDecimal(JsonElement root, string name) =>
        Find(root, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetDecimal(out var d) ? d : null;

    // Hubtel's casing is inconsistent across products, so match property names case-insensitively.
    private static JsonElement? Find(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        return null;
    }
}
