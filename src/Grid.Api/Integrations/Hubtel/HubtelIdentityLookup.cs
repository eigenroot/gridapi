using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Grid.Api.Features.Kyc;
using Microsoft.Extensions.Options;

namespace Grid.Api.Integrations.Hubtel;

/// <summary>
/// Hubtel Verification API, "MSISDN Name Query": the name used to register the SIM.
/// <c>GET /commissionservices/{collectionAccount}/{serviceId}?destination=233…</c>.
/// Hubtel only answers calls from whitelisted server IPs.
/// </summary>
public sealed class HubtelIdentityLookup(HttpClient http, IOptions<KycOptions> options, ILogger<HubtelIdentityLookup> logger)
    : IIdentityLookup
{
    public string Provider => "hubtel";
    public bool Enabled => true;

    public async Task<IdentityLookupResult> LookupNameAsync(string phoneNumber, CancellationToken ct)
    {
        var config = options.Value.Hubtel;
        var url = new Uri(new Uri(config.BaseUrl),
            $"/commissionservices/{Uri.EscapeDataString(config.CollectionAccountNumber)}/{Uri.EscapeDataString(config.MsisdnServiceId)}?destination={Uri.EscapeDataString(phoneNumber)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.ApiId}:{config.ApiKey}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return Interpret(response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Hubtel name lookup failed");
            return new IdentityLookupResult(IdentityLookupOutcome.Retry, Error: ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static IdentityLookupResult Interpret(HttpStatusCode status, string body)
    {
        string? responseCode = null;
        string? message = null;
        string? name = null;

        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                responseCode = Find(root, "ResponseCode") is { ValueKind: JsonValueKind.String } c ? c.GetString() : null;
                message = Find(root, "Message") is { ValueKind: JsonValueKind.String } m ? m.GetString() : null;
                if (Find(root, "Data") is { ValueKind: JsonValueKind.Array } data)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        var display = Find(item, "Display") is { ValueKind: JsonValueKind.String } d ? d.GetString() : null;
                        var value = Find(item, "Value") is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
                        if (string.Equals(display, "name", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                        {
                            name = value.Trim();
                            break;
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        var detail = $"HTTP {(int)status} {responseCode} {message}".Trim();

        if (status == HttpStatusCode.OK && responseCode == "0000")
        {
            return name is not null
                ? new IdentityLookupResult(IdentityLookupOutcome.Found, name)
                : new IdentityLookupResult(IdentityLookupOutcome.NotFound, Error: "No name in response.");
        }

        // 424 means Hubtel's upstream data isn't available yet — try again later.
        if (status == HttpStatusCode.FailedDependency || (int)status >= 500)
            return new IdentityLookupResult(IdentityLookupOutcome.Retry, Error: detail);

        // Documented "not registered / invalid account" answers, and malformed-number rejections.
        if (status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest || responseCode is "3000" or "2001")
            return new IdentityLookupResult(IdentityLookupOutcome.NotFound, Error: detail);

        // 401/403 (credentials, IP whitelist) and anything unexpected: retry, so fixing config recovers.
        return new IdentityLookupResult(IdentityLookupOutcome.Retry, Error: detail);
    }

    private static JsonElement? Find(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        return null;
    }
}
