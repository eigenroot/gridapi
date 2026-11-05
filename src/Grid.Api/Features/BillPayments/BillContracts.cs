using System.Globalization;
using System.Text.RegularExpressions;
using Grid.Api.Common;
using Grid.Api.Features.UtilityAccounts;
using Grid.Api.Integrations.Hubtel;

namespace Grid.Api.Features.BillPayments;

/// <summary>GHS money on the wire: <c>{ "amount": "100.00", "currency": "GHS" }</c>.</summary>
public sealed record MoneyDto(string Amount, string Currency = "GHS")
{
    public static MoneyDto From(decimal amount) => new(amount.ToString("0.00", CultureInfo.InvariantCulture));
    public static MoneyDto? From(decimal? amount) => amount is { } value ? From(value) : null;

    public decimal Parse(string field = "amount")
    {
        if (Currency != "GHS" || Amount is null || !Regex.IsMatch(Amount, @"^[0-9]{1,9}(\.[0-9]{1,2})?$")
            || !decimal.TryParse(Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0)
            throw Errors.Validation([new(field, "Use a positive GHS amount with at most two decimal places.")]);
        return value;
    }
}

// ---- Requests ----

public sealed record LookupRequest(string? ProductCode, string? AccountNumber = null, string? PhoneNumber = null, string? MeterNumber = null);

/// <summary>
/// <c>accountNumber</c> is the meter, smartcard, landline or phone; <c>phoneNumber</c> is the phone an
/// ECG or water account is linked to (default: yours). <c>bundle</c> is required for data and must
/// match one offered at exactly <c>amount</c>.
/// </summary>
public sealed record CreateOrderRequest(
    string? ProductCode,
    MoneyDto? Amount,
    string? AccountNumber = null,
    string? PhoneNumber = null,
    string? MeterNumber = null,
    string? Bundle = null,
    string? Channel = null,
    Guid? SavedAccountId = null,
    Guid? PropertyId = null);

public sealed record ReasonRequest(string? Reason);
public sealed record CompleteRefundRequest(string? Reason, string? ExternalReference, string? Evidence);

// ---- Responses ----

public sealed record AmountRule(MoneyDto? Min, MoneyDto? Max, bool Fixed);

public sealed record ProductResponse(string Code, UtilityType Type, string Name, ProductLookup Lookup, PhoneNetwork? Network,
    bool PhoneDestination, AmountRule AmountRule, bool Enabled);

public sealed record ChannelsResponse(IReadOnlyList<MobileMoneyChannel> Channels, string? Suggested, string PayerPhone);

public sealed record BundleResponse(string Value, string Label, MoneyDto Amount);

/// <summary><c>chargePercent</c> is roughly what Hubtel adds on top of the total when it prompts the wallet.</summary>
public sealed record LookupResponse(string ProductCode, string AccountNumber, string CustomerName, bool NameConfirmed, string BeneficiaryPhone,
    MoneyDto? AmountDue, IReadOnlyList<BundleResponse> Bundles, AmountRule AmountRule, decimal ChargePercent);

public sealed record NextAction(string Kind, string Channel, string MaskedPhone);

public sealed record PaymentSummary(TxnState State, string Channel, string MaskedPhone, MoneyDto? Charges, MoneyDto? AmountCharged,
    DateTimeOffset? PaidAt, string? Error);

public sealed record BillSummary(TxnState State, string ResultKind, string? ExternalReference, DateTimeOffset? DeliveredAt);

/// <summary><c>status</c> is <c>InProgress</c>, <c>NeedsFinance</c> or <c>Completed</c>.</summary>
public sealed record RefundSummary(string Status, MoneyDto Amount, string Reason, DateTimeOffset? CompletedAt);

public sealed record OrderResponse(Guid Id, string Reference, string ProductCode, string AccountNumber, string CustomerName, bool NameConfirmed,
    MoneyDto Principal, MoneyDto Fee, MoneyDto Total, OrderStatus Status, bool NeedsReview, NextAction? NextAction,
    PaymentSummary Payment, BillSummary? Bill, RefundSummary? Refund, Guid? SavedAccountId, Guid? PropertyId, DateTimeOffset CreatedAt);

public sealed record ReceiptBill(string ResultKind, string? ExternalReference, DateTimeOffset? DeliveredAt);

public sealed record ReceiptResponse(Guid OrderId, string Reference, string ProductCode, string AccountNumber, string CustomerName,
    MoneyDto Principal, MoneyDto Fee, MoneyDto Total, MoneyDto? Charges, MoneyDto? AmountCharged, string? PaymentReference,
    DateTimeOffset? PaidAt, ReceiptBill? Bill);

public sealed record CursorPage<T>(IReadOnlyList<T> Items, Guid? NextCursor);

// ---- Finance ----

public sealed record FinanceSummary(MoneyDto Collected, MoneyDto Delivered, MoneyDto RefundsOpen, MoneyDto Refunded,
    int NeedsReview, int UnresolvedPayments, int UnresolvedBills, int OverdueChecks);

public sealed record BillDetail(Guid Id, string ClientReference, TxnState State, int Attempts, string? HubtelTransactionId,
    string? ExternalReference, decimal? Commission, string? ErrorCode, DateTimeOffset? SubmittedAt, DateTimeOffset? DeliveredAt);

public sealed record FinanceRefundResponse(Guid Id, Guid OrderId, MoneyDto Amount, string Reason, TxnState State, RefundMethod Method,
    string? ErrorCode, string? ExternalReference, string? Evidence, Guid? CompletedBy, DateTimeOffset? CompletedAt, DateTimeOffset CreatedAt)
{
    public static FinanceRefundResponse From(Refund r) => new(r.Id, r.OrderId, MoneyDto.From(r.Amount), r.Reason, r.State, r.Method,
        r.ErrorCode, r.ExternalReference, r.Evidence, r.CompletedBy, r.CompletedAt, r.CreatedAt);
}

public sealed record EventResponse(string Kind, string Detail, Guid? ActorId, DateTimeOffset CreatedAt);

public sealed record FinanceOrderResponse(OrderResponse Order, string PayerPhone, string ClientReference, string? HubtelTransactionId,
    string? HubtelOrderId, string? ReviewReason, BillDetail? Bill, FinanceRefundResponse? Refund, IReadOnlyList<EventResponse> Events);

public sealed record FloatTransferSummary(Guid Id, TxnState State, MoneyDto Amount, string ClientReference, string? ErrorCode,
    DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt)
{
    public static FloatTransferSummary From(FloatTransfer t) =>
        new(t.Id, t.State, MoneyDto.From(t.Amount), t.ClientReference, t.ErrorCode, t.CreatedAt, t.ConfirmedAt);
}

public sealed record FloatResponse(MoneyDto? CollectionBalance, MoneyDto? FloatBalance, MoneyDto LowWatermark, MoneyDto Target,
    bool AutoSweep, FloatTransferSummary? Open, FloatTransferSummary? Last);

/// <summary>Entity → response mapping shared by customer and finance views.</summary>
public static class OrderViews
{
    public static OrderResponse Describe(Order o, Bill? bill, Refund? refund)
    {
        var product = BillProducts.Require(o.ProductCode);
        var masked = PhoneNumbers.Mask(o.PayerPhone);
        var waiting = o.Status == OrderStatus.AwaitingPayment && o.PaymentState is TxnState.Submitting or TxnState.Pending or TxnState.Unknown;
        return new(o.Id, Reference(o), o.ProductCode, o.AccountNumber, o.CustomerName, o.NameConfirmed,
            MoneyDto.From(o.Principal), MoneyDto.From(o.Fee), MoneyDto.From(o.Total), o.Status, o.NeedsReview,
            waiting ? new NextAction("ApproveOnPhone", o.Channel, masked) : null,
            new PaymentSummary(o.PaymentState, o.Channel, masked, MoneyDto.From(o.Charges), MoneyDto.From(o.AmountCharged), o.PaidAt, o.PaymentError),
            bill is null ? null : new BillSummary(bill.State, product.ResultKind, bill.ExternalReference, bill.DeliveredAt),
            refund is null ? null : new RefundSummary(RefundStatus(refund), MoneyDto.From(refund.Amount), refund.Reason, refund.CompletedAt),
            o.SavedAccountId, o.PropertyId, o.CreatedAt);
    }

    public static string Reference(Order o) => "GRID-" + o.Id.ToString("N")[^12..].ToUpperInvariant();

    public static string RefundStatus(Refund r) => r.State switch
    {
        TxnState.Succeeded => "Completed",
        TxnState.Failed => "NeedsFinance",
        _ => "InProgress",
    };

    public static BillDetail Detail(Bill b) => new(b.Id, b.ClientReference, b.State, b.Attempts, b.HubtelTransactionId,
        b.ExternalReference, b.Commission, b.ErrorCode, b.SubmittedAt, b.DeliveredAt);
}
