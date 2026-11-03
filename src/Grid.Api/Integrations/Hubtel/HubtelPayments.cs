using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Grid.Api.Common;
using Grid.Api.Features.BillPayments;
using Microsoft.Extensions.Options;

namespace Grid.Api.Integrations.Hubtel;

public enum HubtelOutcome { Pending, Succeeded, Failed, Unknown }

/// <summary>
/// What Hubtel said about one operation. <see cref="Retryable"/> marks a rejection made before
/// anything was processed (no float, credentials), so a fresh attempt cannot double-pay.
/// </summary>
public sealed record HubtelResult(
    HubtelOutcome Outcome,
    string? TransactionId = null,
    decimal? Amount = null,
    string? ErrorCode = null,
    string? OrderId = null,
    string? ExternalReference = null,
    decimal? Charges = null,
    decimal? AmountCharged = null,
    decimal? Commission = null,
    string? Status = null,
    bool Retryable = false,
    bool InsufficientFloat = false)
{
    public bool IsUnpaid => string.Equals(Status, "Unpaid", StringComparison.OrdinalIgnoreCase);
    public bool IsRefunded => string.Equals(Status, "Refunded", StringComparison.OrdinalIgnoreCase);
}

public sealed record MobileMoneyChannel(string Code, string Name, PhoneNetwork Network);

/// <summary>One account the biller knows; an ECG phone lookup can return several meters.</summary>
public sealed record BeneficiaryCandidate(string AccountNumber, string CustomerName, decimal? AmountDue);

public sealed record BundleOption(string Value, string Label, decimal Amount);

public sealed record BeneficiaryLookup(
    IReadOnlyList<BeneficiaryCandidate> Candidates,
    string? SessionId = null,
    string? Bundle = null,
    IReadOnlyList<BundleOption>? Bundles = null);

/// <summary>
/// Hubtel's payment APIs: Direct Receive Money (rmp), Commission Services (cs), Transaction Status
/// (api-txnstatus), Refund (refund-api) and Balance &amp; Transfers (trnf). Everything except
/// Commission Services lookups only answers whitelisted server IPs. Nothing here retries; callers
/// decide what an unclear answer means.
/// </summary>
public sealed class HubtelPayments(HttpClient http, IOptions<BillPaymentOptions> options, ILogger<HubtelPayments> logger)
{
    private HubtelPaymentOptions Config => options.Value.Hubtel;

    public static readonly IReadOnlyList<MobileMoneyChannel> Channels =
    [
        new("mtn-gh", "MTN Mobile Money", PhoneNetwork.Mtn),
        new("vodafone-gh", "Telecel Cash", PhoneNetwork.Telecel),
        new("tigo-gh", "AirtelTigo Money", PhoneNetwork.AirtelTigo),
    ];

    public static string? SuggestChannel(string phone) =>
        Channels.FirstOrDefault(c => c.Network == PhoneNumbers.GuessNetwork(phone))?.Code;

    public async Task<BeneficiaryLookup> LookupAsync(BillProduct product, string destination, string? mobile, CancellationToken ct)
    {
        var query = $"?destination={Esc(destination)}" + (mobile is null ? "" : $"&mobile={Esc(mobile)}");
        var reply = await SendAsync(HttpMethod.Get, Config.CommissionBaseUrl,
            $"/commissionservices/{Esc(Config.DisbursementAccountNumber)}/{Esc(Config.ServiceId(product))}{query}", null, ct);
        return InterpretLookup(product, destination, reply.Status, reply.Body);
    }

    /// <summary>Prompts the customer's wallet. Hubtel adds its charge on top of <paramref name="amount"/>.</summary>
    public async Task<HubtelResult> ReceiveMoneyAsync(string reference, string phone, string channel, decimal amount, string description,
        string? customerName, string? email, string callbackUrl, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["CustomerName"] = customerName, ["CustomerMsisdn"] = phone, ["CustomerEmail"] = email, ["Channel"] = channel,
            ["Amount"] = Money(amount), ["PrimaryCallbackUrl"] = callbackUrl, ["Description"] = description, ["ClientReference"] = reference,
        };
        var reply = await SendAsync(HttpMethod.Post, Config.ReceiveMoneyBaseUrl,
            $"/merchantaccount/merchants/{Esc(Config.CollectionAccountNumber)}/receive/mobilemoney", body, ct);
        return InterpretReceiveMoney(reply.Status, reply.Body);
    }

    /// <summary>Pays a bill from the Disbursement float.</summary>
    public async Task<HubtelResult> PayBillAsync(BillProduct product, string reference, string destination, decimal amount,
        string? bundle, string? sessionId, string? email, string callbackUrl, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["Destination"] = destination, ["Amount"] = Money(amount), ["CallbackUrl"] = callbackUrl, ["ClientReference"] = reference,
        };
        var extra = new Dictionary<string, object?>();
        if (bundle is not null) extra["bundle"] = bundle;
        if (product.Lookup == ProductLookup.WaterMeter) { extra["Email"] = email; extra["SessionId"] = sessionId; }
        if (extra.Count > 0) body["Extradata"] = extra;
        var reply = await SendAsync(HttpMethod.Post, Config.CommissionBaseUrl,
            $"/commissionservices/{Esc(Config.DisbursementAccountNumber)}/{Esc(Config.ServiceId(product))}", body, ct);
        return InterpretPayBill(reply.Status, reply.Body);
    }

    /// <summary>Status of a receive-money or bill payment, by our client reference.</summary>
    public async Task<HubtelResult> GetStatusAsync(string reference, CancellationToken ct)
    {
        var reply = await SendAsync(HttpMethod.Get, Config.StatusBaseUrl,
            $"/transactions/{Esc(Config.CollectionAccountNumber)}/status?clientReference={Esc(reference)}", null, ct);
        return InterpretStatus(reference, reply.Status, reply.Body);
    }

    public async Task<HubtelResult> RefundAsync(string hubtelOrderId, string callbackUrl, CancellationToken ct)
    {
        var reply = await SendAsync(HttpMethod.Post, Config.RefundBaseUrl,
            $"/refund/{Esc(Config.CollectionAccountNumber)}/order/{Esc(hubtelOrderId)}", new Dictionary<string, object?> { ["callbackUrl"] = callbackUrl }, ct);
        return InterpretRefund(reply.Status, reply.Body);
    }

    public async Task<decimal?> GetCollectionBalanceAsync(CancellationToken ct) =>
        Balance(await SendAsync(HttpMethod.Get, Config.TransferBaseUrl, $"/api/inter-transfers/{Esc(Config.CollectionAccountNumber)}", null, ct));

    public async Task<decimal?> GetFloatBalanceAsync(CancellationToken ct) =>
        Balance(await SendAsync(HttpMethod.Get, Config.TransferBaseUrl, $"/api/inter-transfers/prepaid/{Esc(Config.DisbursementAccountNumber)}", null, ct));

    public async Task<HubtelResult> TransferToFloatAsync(string reference, decimal amount, string callbackUrl, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["Description"] = "GRID bill payment float", ["Amount"] = Money(amount), ["ClientReference"] = reference,
            ["DestinationAccountNumber"] = Config.DisbursementAccountNumber, ["PrimaryCallbackUrl"] = callbackUrl,
        };
        var reply = await SendAsync(HttpMethod.Post, Config.TransferBaseUrl, $"/api/inter-transfers/{Esc(Config.CollectionAccountNumber)}", body, ct);
        return InterpretTransfer(reply.Status, reply.Body);
    }

    public async Task<HubtelResult> GetTransferStatusAsync(string reference, CancellationToken ct)
    {
        var reply = await SendAsync(HttpMethod.Get, Config.TransferBaseUrl,
            $"/api/inter-transfers/status/{Esc(Config.CollectionAccountNumber)}?clientReference={Esc(reference)}", null, ct);
        return InterpretTransferStatus(reference, reply.Status, reply.Body);
    }

    // ---- Interpretation: static so tests can replay Hubtel's documented samples ----

    internal static BeneficiaryLookup InterpretLookup(BillProduct product, string destination, HttpStatusCode? status, string? body)
    {
        var root = Parse(body);
        if (status == HttpStatusCode.OK && Text(root, "ResponseCode") == "0000" && Field(root, "Data") is { ValueKind: JsonValueKind.Array } data)
        {
            var rows = data.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(x => (Display: (Text(x, "Display") ?? "").Trim(), Value: (Text(x, "Value") ?? "").Trim(), Amount: Decimal(x, "Amount")))
                .ToList();
            string? Row(params string[] keys) =>
                rows.FirstOrDefault(r => keys.Any(k => string.Equals(r.Display, k, StringComparison.OrdinalIgnoreCase))).Value is { Length: > 0 } v ? v : null;
            decimal? Due() => decimal.TryParse(Row("amountDue"), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

            switch (product.Lookup)
            {
                case ProductLookup.PhoneMeters:
                    return new(rows.Where(r => r.Value.Length > 0)
                        .Select(r => new BeneficiaryCandidate(r.Value.ToUpperInvariant(), MeterOwner(r.Display, r.Value), r.Amount)).ToList());
                case ProductLookup.WaterMeter:
                    var session = Row("sessionId") ?? throw Unavailable();
                    return new([new(destination, Row("name") ?? "", Due())], SessionId: session);
                case ProductLookup.Account:
                    var account = Row("account", "Account Number") ?? destination;
                    return new([new(account, Row("name") ?? "", Due())], Bundle: account);
                case ProductLookup.Bundles:
                    return new([new(destination, "", null)], Bundles: rows.Where(r => r.Value.Length > 0 && r.Amount is > 0)
                        .Select(r => new BundleOption(r.Value, BundleLabel(product, r.Display, r.Value), r.Amount!.Value)).ToList());
            }
        }
        if (status is HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.NotFound && root.ValueKind == JsonValueKind.Object)
            throw new ApiException(404, "bills.account_not_found", "We couldn't find that account. Check the number and try again.");
        throw Unavailable();
    }

    internal static HubtelResult InterpretReceiveMoney(HttpStatusCode? status, string? body)
    {
        var root = Parse(body);
        var code = Text(root, "ResponseCode");
        var data = Field(root, "Data");
        if (code is "0001" or "0000")
            return new(HubtelOutcome.Pending, Trim(Text(data, "TransactionId")), Decimal(data, "Amount"),
                OrderId: Trim(Text(data, "OrderId") ?? Text(data, "TransactionId")), Charges: Decimal(data, "Charges"),
                AmountCharged: Decimal(data, "AmountCharged"));
        // Documented rejections happen before any prompt reaches the customer.
        if (code is "2001" or "4000" or "4010" or "4070" or "4101" or "4103") return new(HubtelOutcome.Failed, ErrorCode: $"hubtel.{code}");
        if (code is null && status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return new(HubtelOutcome.Failed, ErrorCode: "hubtel.forbidden");
        return new(HubtelOutcome.Unknown, ErrorCode: code is null ? "hubtel.unconfirmed" : $"hubtel.{code}");
    }

    internal static HubtelResult InterpretPayBill(HttpStatusCode? status, string? body)
    {
        var root = Parse(body);
        var code = Text(root, "ResponseCode");
        var data = Field(root, "Data");
        // 0000 here is acceptance too; delivery is confirmed by callback or status check.
        if (code is "0001" or "0000")
            return new(HubtelOutcome.Pending, Trim(Text(data, "TransactionId")), Decimal(data, "Amount"), Commission: Decimal(Field(data, "Meta"), "Commission"));
        if (code == "4075") return new(HubtelOutcome.Failed, ErrorCode: "hubtel.4075", Retryable: true, InsufficientFloat: true);
        // Credential and permission problems: nothing was processed, so it is safe to try again later.
        if (code is "4101" or "4103") return new(HubtelOutcome.Failed, ErrorCode: $"hubtel.{code}", Retryable: true);
        if (code is null && status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new(HubtelOutcome.Failed, ErrorCode: "hubtel.forbidden", Retryable: true);
        if (code is "2000" or "2001" or "4000" or "4010") return new(HubtelOutcome.Failed, ErrorCode: $"hubtel.{code}");
        // 0005 is Hubtel's own "state unknown"; a transport error is the same for us.
        return new(HubtelOutcome.Unknown, ErrorCode: code is null ? "hubtel.unconfirmed" : $"hubtel.{code}");
    }

    internal static HubtelResult InterpretStatus(string reference, HttpStatusCode? status, string? body)
    {
        var root = Parse(body);
        var data = Field(root, "data");
        if (status != HttpStatusCode.OK || Text(root, "responseCode") != "0000" || data.ValueKind != JsonValueKind.Object)
            return new(HubtelOutcome.Unknown, ErrorCode: "hubtel.status_unconfirmed");
        if (Trim(Text(data, "clientReference")) is { } echoed && echoed != reference)
            return new(HubtelOutcome.Unknown, ErrorCode: "hubtel.status_reference_mismatch");
        var state = Text(data, "status");
        var outcome = state?.ToLowerInvariant() switch
        {
            "paid" or "refunded" => HubtelOutcome.Succeeded,
            "unpaid" => HubtelOutcome.Pending,
            _ => HubtelOutcome.Unknown,
        };
        return new(outcome, Trim(Text(data, "transactionId")), Decimal(data, "amount"),
            ExternalReference: Text(data, "externalTransactionId"), Charges: Decimal(data, "charges"), Status: state);
    }

    internal static HubtelResult InterpretRefund(HttpStatusCode? status, string? body)
    {
        var code = Text(Parse(body), "responseCode");
        return code switch
        {
            "0001" or "0000" => new(HubtelOutcome.Pending),
            // A refund for this order is already in progress: wait for its callback.
            "4515" => new(HubtelOutcome.Pending, ErrorCode: "hubtel.4515"),
            "3000" or "4000" or "4509" => new(HubtelOutcome.Failed, ErrorCode: $"hubtel.{code}"),
            null when status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(HubtelOutcome.Failed, ErrorCode: "hubtel.forbidden"),
            _ => new(HubtelOutcome.Unknown, ErrorCode: code is null ? "hubtel.unconfirmed" : $"hubtel.{code}"),
        };
    }

    internal static HubtelResult InterpretTransfer(HttpStatusCode? status, string? body)
    {
        var root = Parse(body);
        var code = Text(root, "responseCode");
        if (code is "0001" or "0000") return new(HubtelOutcome.Pending, Amount: Decimal(Field(root, "data"), "amount"));
        if (code == "4000" || (code is null && status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            return new(HubtelOutcome.Failed, ErrorCode: code is null ? $"hubtel.http_{(int)status!}" : $"hubtel.{code}");
        return new(HubtelOutcome.Unknown, ErrorCode: code is null ? "hubtel.unconfirmed" : $"hubtel.{code}");
    }

    internal static HubtelResult InterpretTransferStatus(string reference, HttpStatusCode? status, string? body)
    {
        var data = Field(Parse(body), "data");
        if (status != HttpStatusCode.OK || data.ValueKind != JsonValueKind.Object || Trim(Text(data, "clientReference")) != reference)
            return new(HubtelOutcome.Unknown, ErrorCode: "hubtel.status_unconfirmed");
        return Text(data, "status")?.ToLowerInvariant() switch
        {
            "success" => new(HubtelOutcome.Succeeded, Text(data, "id"), Decimal(data, "transferAmount")),
            "failed" => new(HubtelOutcome.Failed, Text(data, "id"), Decimal(data, "transferAmount"), ErrorCode: "hubtel.transfer_failed"),
            _ => new(HubtelOutcome.Pending, Text(data, "id")),
        };
    }

    /// <summary>Reads the body Hubtel POSTs to a callback URL; the shape differs a little per API.</summary>
    public static HubtelResult InterpretCallback(JsonElement root, out string? clientReference)
    {
        var data = Field(root, "Data");
        var code = Text(root, "ResponseCode");
        clientReference = Trim(Text(data, "ClientReference"));
        var common = new HubtelResult(code == "0000" ? HubtelOutcome.Succeeded : HubtelOutcome.Failed,
            Trim(Text(data, "TransactionId")), Decimal(data, "Amount"), code == "0000" ? null : $"hubtel.{code ?? "failed"}",
            Trim(Text(data, "OrderId") ?? Text(data, "TransactionId")), Text(data, "ExternalTransactionId"),
            Decimal(data, "Charges"), Decimal(data, "AmountCharged"), Decimal(Field(data, "Meta"), "Commission"));
        return code == "4075" ? common with { Retryable = true, InsufficientFloat = true } : common;
    }

    private decimal? Balance((HttpStatusCode? Status, string? Body) reply)
    {
        var root = Parse(reply.Body);
        if (reply.Status == HttpStatusCode.OK && Text(root, "responseCode") == "0000") return Decimal(Field(root, "data"), "amount");
        logger.LogWarning("Hubtel balance query was not answered: HTTP {Status} {Code}", (int?)reply.Status, Text(root, "responseCode"));
        return null;
    }

    /// <summary>"THOMAS ANANE (G131099826)" → "THOMAS ANANE".</summary>
    private static string MeterOwner(string display, string meter)
    {
        var suffix = $"({meter})";
        return (display.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? display[..^suffix.Length] : display).Trim();
    }

    /// <summary>Telecel puts the plan in Display and the size in Value; the other telcos put both in Display.</summary>
    private static string BundleLabel(BillProduct product, string display, string value) =>
        product.Network == PhoneNetwork.Telecel && !display.Contains(value, StringComparison.OrdinalIgnoreCase) ? $"{display} · {value}" : display;

    // ---- HTTP ----

    private async Task<(HttpStatusCode? Status, string? Body)> SendAsync(HttpMethod method, string baseUrl, string pathAndQuery, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUrl.TrimEnd('/') + pathAndQuery));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Config.ApiId}:{Config.ApiKey}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Config.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            // Even a cancelled call may have been accepted by Hubtel; the caller treats this as unknown.
            logger.LogWarning("Hubtel {Method} {Path} got no answer: {Error}", method, pathAndQuery.Split('?')[0], ex.GetType().Name);
            return (null, null);
        }
    }

    private static string Esc(string value) => Uri.EscapeDataString(value);
    private static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static string? Trim(string? value) => value?.Trim();

    private static JsonElement Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return default;
        try { return JsonDocument.Parse(body).RootElement.Clone(); }
        catch (JsonException) { return default; }
    }

    private static JsonElement Field(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return default;
        foreach (var property in obj.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }

    private static string? Text(JsonElement obj, string name) =>
        Field(obj, name) is var value && value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : null;

    private static decimal? Decimal(JsonElement obj, string name) =>
        decimal.TryParse(Text(obj, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static ApiException Unavailable() =>
        new(503, "payments.provider_unavailable", "We couldn't reach the biller just now. Try again shortly.");
}
