using System.Net;
using Grid.Api.Common;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.BillPayments;

public sealed class BillPaymentOptions
{
    public const string Section = "Payments";

    /// <summary>Lets customers place orders. Off by default.</summary>
    public bool Enabled { get; set; }
    /// <summary>Whether this process runs the payment worker (tests drive it directly).</summary>
    public bool RunWorker { get; set; }
    public int PollSeconds { get; set; } = 2;
    /// <summary>The public HTTPS origin Hubtel calls back, e.g. https://grid-api.eigenroot.com.</summary>
    public string CallbackBaseUrl { get; set; } = "";

    /// <summary>GRID's own fee. Zero for now.</summary>
    public decimal FeeFlat { get; set; }
    public decimal FeePercent { get; set; }
    /// <summary>Hubtel's receive-money charge, which the payer bears. Only shown to customers as a hint.</summary>
    public decimal ChargePercent { get; set; }
    public decimal MinAmount { get; set; } = 1m;
    public decimal MaxAmount { get; set; } = 1000m;
    /// <summary>Product codes to pause, e.g. while rolling out one at a time.</summary>
    public List<string> DisabledProducts { get; set; } = [];

    /// <summary>Hubtel asks for a status check only once a callback is this overdue.</summary>
    public int StatusCheckDelaySeconds { get; set; } = 300;
    /// <summary>A payment still "Unpaid" this long after the prompt is treated as declined.</summary>
    public int PaymentUnpaidFailMinutes { get; set; } = 15;
    /// <summary>A bill payment still "Unpaid" this long after sending is treated as failed, which refunds the customer.</summary>
    public int BillUnpaidFailMinutes { get; set; } = 60;
    public int BillRetrySeconds { get; set; } = 120;
    public int MaxBillAttempts { get; set; } = 10;
    /// <summary>Anything unresolved this long is flagged for review and then checked daily.</summary>
    public int ReviewAfterHours { get; set; } = 24;

    /// <summary>Hubtel's Refund API refuses orders under GHS 1 or older than 45 days.</summary>
    public decimal RefundMinimum { get; set; } = 1m;
    public int RefundWindowDays { get; set; } = 45;

    public FloatOptions Float { get; set; } = new();

    /// <summary>Phone numbers allowed to use /ops/finance.</summary>
    public List<string> FinanceOperators { get; set; } = [];

    public HubtelPaymentOptions Hubtel { get; set; } = new();

    public void RequireEnabled()
    {
        if (!Enabled) throw new ApiException(503, "payments.disabled", "Bill payments are not available yet.");
    }

    public bool IsEnabled(BillProduct product) => !DisabledProducts.Contains(product.Code, StringComparer.Ordinal);

    public TimeSpan StatusCheckDelay => TimeSpan.FromSeconds(StatusCheckDelaySeconds);
}

/// <summary>Keeps the Disbursement account, which pays bills, topped up from customer payments.</summary>
public sealed class FloatOptions
{
    public bool AutoSweep { get; set; } = true;
    /// <summary>Sweep when the float falls below this.</summary>
    public decimal LowWatermark { get; set; } = 500m;
    /// <summary>Sweep up to this float balance.</summary>
    public decimal Target { get; set; } = 2000m;
    /// <summary>Always leave this much in the Collection account, for refunds.</summary>
    public decimal CollectionReserve { get; set; } = 200m;
    public decimal MinTransfer { get; set; } = 1m;
    public int CheckMinutes { get; set; } = 10;
}

public sealed class HubtelPaymentOptions
{
    /// <summary>Hubtel Programmable API key id/secret, sent as Basic auth.</summary>
    public string ApiId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    /// <summary>Receives customer payments; also used for status checks, refunds and transfers.</summary>
    public string CollectionAccountNumber { get; set; } = "";
    /// <summary>The prepaid float that Commission Services debits to pay bills.</summary>
    public string DisbursementAccountNumber { get; set; } = "";
    /// <summary>Signs callback URLs. At least 32 characters, generated separately.</summary>
    public string CallbackSecret { get; set; } = "";
    /// <summary>Where Hubtel calls back from. Empty means its documented address.</summary>
    public List<string> CallbackIps { get; set; } = [];
    /// <summary>Reject callbacks from other addresses instead of only logging them.</summary>
    public bool EnforceCallbackIp { get; set; }
    /// <summary>Ghana Water requires an email; this is used when the customer has none.</summary>
    public string FallbackReceiptEmail { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 20;

    public string ReceiveMoneyBaseUrl { get; set; } = "https://rmp.hubtel.com";
    public string CommissionBaseUrl { get; set; } = "https://cs.hubtel.com";
    public string StatusBaseUrl { get; set; } = "https://api-txnstatus.hubtel.com";
    public string RefundBaseUrl { get; set; } = "https://refund-api.hubtel.com";
    public string TransferBaseUrl { get; set; } = "https://trnf.hubtel.com";
    /// <summary>Overrides of <see cref="BillProduct.ServiceId"/>, by product code.</summary>
    public Dictionary<string, string> ServiceIds { get; set; } = new();

    public static readonly string[] DocumentedCallbackIps = ["18.202.122.131"];

    public string ServiceId(BillProduct product) =>
        ServiceIds.TryGetValue(product.Code, out var id) && !string.IsNullOrWhiteSpace(id) ? id : product.ServiceId;

    public IEnumerable<string> BaseUrls() => [ReceiveMoneyBaseUrl, CommissionBaseUrl, StatusBaseUrl, RefundBaseUrl, TransferBaseUrl];
}

public sealed class BillPaymentOptionsValidator(IHostEnvironment environment) : IValidateOptions<BillPaymentOptions>
{
    public ValidateOptionsResult Validate(string? name, BillPaymentOptions o)
    {
        if (!o.Enabled && !o.RunWorker) return ValidateOptionsResult.Success;

        var errors = new List<string>();
        var h = o.Hubtel;
        if (string.IsNullOrWhiteSpace(h.ApiId) || string.IsNullOrWhiteSpace(h.ApiKey)
            || string.IsNullOrWhiteSpace(h.CollectionAccountNumber) || string.IsNullOrWhiteSpace(h.DisbursementAccountNumber))
            errors.Add("Payments:Hubtel needs ApiId, ApiKey, CollectionAccountNumber and DisbursementAccountNumber.");
        if (h.CallbackSecret.Length < 32) errors.Add("Payments:Hubtel:CallbackSecret must be at least 32 characters.");
        if (h.TimeoutSeconds is < 1 or > 60) errors.Add("Payments:Hubtel:TimeoutSeconds must be 1–60.");
        if (h.CallbackIps.Any(ip => !IPAddress.TryParse(ip, out _))) errors.Add("Payments:Hubtel:CallbackIps has an invalid address.");
        if (h.BaseUrls().Append(o.CallbackBaseUrl).Any(url => !ValidUrl(url)))
            errors.Add("Payments needs HTTPS CallbackBaseUrl and Hubtel base URLs (loopback HTTP is allowed only in Testing).");
        if (o.MinAmount <= 0 || o.MaxAmount < o.MinAmount || o.FeeFlat < 0 || o.FeePercent is < 0 or > 100
            || o.ChargePercent is < 0 or > 100 || decimal.Round(o.FeeFlat, 2) != o.FeeFlat || o.RefundMinimum < 0)
            errors.Add("Payments amounts and fees are invalid.");
        if (o.PollSeconds < 1 || o.StatusCheckDelaySeconds < 0 || o.PaymentUnpaidFailMinutes < 1 || o.BillUnpaidFailMinutes < 1
            || o.BillRetrySeconds < 1 || o.MaxBillAttempts < 1 || o.ReviewAfterHours < 1 || o.RefundWindowDays < 1)
            errors.Add("Payments timing settings must be positive.");
        var f = o.Float;
        if (f.LowWatermark < 0 || f.Target < f.LowWatermark || f.CollectionReserve < 0 || f.MinTransfer <= 0 || f.CheckMinutes < 1)
            errors.Add("Payments:Float needs 0 ≤ LowWatermark ≤ Target, a non-negative reserve and a positive minimum transfer.");
        foreach (var code in o.DisabledProducts.Where(c => BillProducts.Find(c) is null))
            errors.Add($"Payments:DisabledProducts has an unknown product code '{code}'.");
        foreach (var phone in o.FinanceOperators.Where(p => !PhoneNumbers.TryNormalize(p, out _)))
            errors.Add($"Payments:FinanceOperators has an invalid phone number '{phone}'.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private bool ValidUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || (environment.IsEnvironment("Testing") && uri.IsLoopback && uri.Scheme == "http"))
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
