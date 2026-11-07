using Grid.Api.Data;
using Grid.Api.Integrations.Hubtel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.BillPayments;

/// <summary>
/// Every state change for orders, bills, refunds and float transfers. Callers load and lock the
/// order first, apply a Hubtel answer here, then save, so a callback and a status check go through
/// the same rules. Nothing here talks to Hubtel.
/// </summary>
public sealed class OrderTransitions(GridDbContext db, IOptions<BillPaymentOptions> options, TimeProvider time, ILogger<OrderTransitions> logger)
{
    private static readonly int[] BackoffSeconds = [30, 120, 300, 900, 1800, 3600];

    private BillPaymentOptions Config => options.Value;
    private DateTimeOffset Now => time.GetUtcNow();

    /// <summary>Set when a bill was refused for lack of float; the caller should sweep.</summary>
    public bool FloatSweepRequested { get; private set; }

    public static bool Terminal(TxnState state) => state is TxnState.Succeeded or TxnState.Failed;

    // ---- Payment (the customer's mobile money) ----

    /// <summary>Hubtel's immediate answer to the prompt request.</summary>
    public void PaymentSubmitted(Order order, HubtelResult result)
    {
        order.HubtelTransactionId ??= result.TransactionId;
        order.HubtelOrderId ??= result.OrderId;
        order.Charges ??= result.Charges;
        order.AmountCharged ??= result.AmountCharged;
        if (order.PaymentState != TxnState.Submitting) return; // A callback beat the response.

        switch (result.Outcome)
        {
            case HubtelOutcome.Failed:
                PaymentFailed(order, result.ErrorCode);
                break;
            default:
                order.PaymentState = result.Outcome == HubtelOutcome.Unknown ? TxnState.Unknown : TxnState.Pending;
                order.PaymentError = result.Outcome == HubtelOutcome.Unknown ? result.ErrorCode : null;
                order.NextCheckAt = Now + Config.StatusCheckDelay;
                break;
        }
        Event(order.Id, "PaymentSubmitted", $"{order.PaymentState} {result.ErrorCode}".Trim());
    }

    /// <summary>
    /// Whether Hubtel took the order's total. Depending on how the account bears charges, Hubtel
    /// reports <c>Amount</c> either as what we asked for or with its charge already added on.
    /// </summary>
    private static bool CoversTotal(Order order, HubtelResult result) =>
        result.Amount == order.Total || (result.Charges is { } charges && result.Amount - charges == order.Total);

    /// <summary>The payment's outcome, from a callback or a status check.</summary>
    public async Task PaymentOutcomeAsync(Order order, HubtelResult result, CancellationToken ct)
    {
        if (result.Outcome == HubtelOutcome.Pending && result.IsUnpaid
            && Now - order.CreatedAt >= TimeSpan.FromMinutes(Config.PaymentUnpaidFailMinutes))
            result = result with { Outcome = HubtelOutcome.Failed, ErrorCode = "hubtel.unpaid" };

        switch (result.Outcome)
        {
            case HubtelOutcome.Succeeded when order.PaymentState == TxnState.Succeeded:
                if ((result.Amount is { } again && again != order.AmountPaid && !CoversTotal(order, result))
                    || (result.TransactionId is { } id && order.HubtelTransactionId is { } known && id != known))
                    Review(order, "payment_evidence_conflict");
                return;

            case HubtelOutcome.Succeeded:
                var wasFailed = order.PaymentState == TxnState.Failed;
                order.PaymentState = TxnState.Succeeded;
                order.PaidAt = Now;
                order.AmountPaid = result.Amount;
                order.HubtelTransactionId = result.TransactionId ?? order.HubtelTransactionId;
                order.HubtelOrderId ??= result.OrderId ?? result.TransactionId;
                order.Charges ??= result.Charges;
                order.AmountCharged ??= result.AmountCharged;
                order.PaymentError = null;
                order.NextCheckAt = null;
                Event(order.Id, "PaymentConfirmed", Money(result.Amount));

                if (result.Amount is not { } paid) Review(order, "payment_amount_missing");
                else if (!CoversTotal(order, result))
                {
                    Review(order, "payment_amount_mismatch");
                    await OpenRefundAsync(order, paid, "AmountMismatch", ct);
                }
                // They may already have paid again on a new order; this money goes back.
                else if (wasFailed) await OpenRefundAsync(order, paid, "LatePayment", ct);
                else
                {
                    order.Status = OrderStatus.Processing;
                    var bill = new Bill { OrderId = order.Id, ClientReference = "", Amount = order.Principal, State = TxnState.Created, NextCheckAt = Now };
                    bill.ClientReference = BillReference(bill);
                    db.Bills.Add(bill);
                }
                return;

            case HubtelOutcome.Failed:
                if (order.PaymentState == TxnState.Succeeded) Review(order, "payment_evidence_conflict");
                else if (order.PaymentState != TxnState.Failed) PaymentFailed(order, result.ErrorCode);
                return;

            default:
                if (Terminal(order.PaymentState)) return;
                order.PaymentState = result.Outcome == HubtelOutcome.Pending ? TxnState.Pending : TxnState.Unknown;
                Recheck(order, order, order.CreatedAt);
                return;
        }
    }

    private void PaymentFailed(Order order, string? code)
    {
        order.PaymentState = TxnState.Failed;
        order.PaymentError = code ?? "hubtel.declined";
        order.NextCheckAt = null;
        if (order.Status == OrderStatus.AwaitingPayment) order.Status = OrderStatus.PaymentFailed;
        Event(order.Id, "PaymentFailed", order.PaymentError);
    }

    // ---- Bill (paying the biller from the float) ----

    /// <summary>The reference for the bill's next attempt. Each attempt needs its own.</summary>
    public static string BillReference(Bill bill) => $"B{bill.Id:N}{bill.Attempts + 1}";

    public async Task BillSubmittedAsync(Order order, Bill bill, HubtelResult result, CancellationToken ct)
    {
        bill.HubtelTransactionId = result.TransactionId ?? bill.HubtelTransactionId;
        bill.Commission ??= result.Commission;
        if (bill.State != TxnState.Submitting) return;
        if (result.Outcome == HubtelOutcome.Failed) { await BillFailedAsync(order, bill, result, ct); return; }
        bill.State = result.Outcome == HubtelOutcome.Unknown ? TxnState.Unknown : TxnState.Pending;
        bill.ErrorCode = result.Outcome == HubtelOutcome.Unknown ? result.ErrorCode : null;
        bill.NextCheckAt = Now + Config.StatusCheckDelay;
        Event(order.Id, "BillSubmitted", $"{bill.State} {result.ErrorCode}".Trim());
    }

    public async Task BillOutcomeAsync(Order order, Bill bill, HubtelResult result, CancellationToken ct)
    {
        if (result.Outcome == HubtelOutcome.Pending && result.IsUnpaid
            && Now - (bill.SubmittedAt ?? bill.CreatedAt) >= TimeSpan.FromMinutes(Config.BillUnpaidFailMinutes))
            result = result with { Outcome = HubtelOutcome.Failed, ErrorCode = "hubtel.unpaid", Retryable = false };

        switch (result.Outcome)
        {
            case HubtelOutcome.Succeeded when result.Amount is { } amount && amount != bill.Amount:
                Review(order, "bill_amount_mismatch");
                return;

            case HubtelOutcome.Succeeded when bill.State == TxnState.Succeeded:
                if (result.TransactionId is { } id && bill.HubtelTransactionId is { } known && id != known) Review(order, "bill_evidence_conflict");
                return;

            case HubtelOutcome.Succeeded:
                var previous = bill.State;
                bill.State = TxnState.Succeeded;
                bill.DeliveredAt = Now;
                bill.HubtelTransactionId = result.TransactionId ?? bill.HubtelTransactionId;
                bill.ExternalReference = result.ExternalReference ?? bill.ExternalReference;
                bill.Commission = result.Commission ?? bill.Commission;
                bill.ErrorCode = null;
                bill.NextCheckAt = null;
                Event(order.Id, "BillDelivered", bill.ExternalReference ?? "");

                var refund = await RefundForAsync(order.Id, ct);
                if (refund is null) order.Status = OrderStatus.Completed;
                else if (refund.State == TxnState.Created)
                {
                    // Hubtel contradicted its earlier failure before the money went back: keep the bill, stop the refund.
                    refund.State = TxnState.Failed; refund.ErrorCode = "refund.bill_delivered"; refund.NextCheckAt = null;
                    order.Status = OrderStatus.Completed;
                    Review(order, "bill_delivered_after_failure");
                }
                else Review(order, previous == TxnState.Failed ? "bill_delivered_and_refunded" : "bill_delivered_during_refund");
                return;

            case HubtelOutcome.Failed:
                if (bill.State == TxnState.Succeeded) Review(order, "bill_evidence_conflict");
                else if (bill.State != TxnState.Failed) await BillFailedAsync(order, bill, result, ct);
                return;

            default:
                if (Terminal(bill.State)) return;
                bill.State = result.Outcome == HubtelOutcome.Pending ? TxnState.Pending : TxnState.Unknown;
                Recheck(order, bill, bill.SubmittedAt ?? bill.CreatedAt);
                return;
        }
    }

    /// <summary>
    /// A rejection Hubtel made before processing gets another attempt with a new reference; any other
    /// failure, or too many attempts, returns the customer's money.
    /// </summary>
    public async Task BillFailedAsync(Order order, Bill bill, HubtelResult result, CancellationToken ct)
    {
        bill.ErrorCode = result.ErrorCode ?? "hubtel.bill_failed";
        var withinWindow = Now - (order.PaidAt ?? bill.CreatedAt) < TimeSpan.FromHours(Config.ReviewAfterHours);
        if (result.Retryable && bill.Attempts < Config.MaxBillAttempts && withinWindow)
        {
            bill.State = TxnState.Created;
            bill.Checks = 0;
            bill.NextCheckAt = Now.AddSeconds(Config.BillRetrySeconds);
            FloatSweepRequested |= result.InsufficientFloat;
            Event(order.Id, "BillRetryScheduled", $"{bill.ClientReference} {bill.ErrorCode}");
            return;
        }
        bill.State = TxnState.Failed;
        bill.NextCheckAt = null;
        Event(order.Id, "BillFailed", $"{bill.ClientReference} {bill.ErrorCode}");
        await OpenRefundAsync(order, order.AmountPaid ?? order.Total, "BillFailed", ct);
    }

    // ---- Refund ----

    private async Task OpenRefundAsync(Order order, decimal amount, string reason, CancellationToken ct)
    {
        if (await RefundForAsync(order.Id, ct) is not null) return;
        db.Refunds.Add(new Refund { OrderId = order.Id, Amount = amount, Reason = reason, State = TxnState.Created, Method = RefundMethod.Hubtel, NextCheckAt = Now });
        order.Status = OrderStatus.Refunding;
        Event(order.Id, "RefundOpened", $"{reason} {Money(amount)}");
    }

    /// <summary>Why Hubtel's Refund API can't (or mustn't) return this money now, or null.</summary>
    public string? RefundBlocker(Order order, Refund refund, Bill? bill) =>
        order.HubtelOrderId is null ? "refund.no_hubtel_order"
        : refund.Amount < Config.RefundMinimum ? "refund.below_minimum"
        : order.PaidAt is { } paid && Now - paid > TimeSpan.FromDays(Config.RefundWindowDays) ? "refund.window_passed"
        // Never return money while the bill it paid for might still go through.
        : bill is not null && bill.State != TxnState.Failed ? "refund.bill_unresolved"
        : null;

    public void RefundSubmitted(Order order, Refund refund, HubtelResult result)
    {
        if (refund.State != TxnState.Submitting) return;
        if (result.Outcome == HubtelOutcome.Failed) { NeedsFinance(order, refund, result.ErrorCode ?? "hubtel.refund_rejected"); return; }
        refund.State = result.Outcome == HubtelOutcome.Unknown ? TxnState.Unknown : TxnState.Pending;
        refund.ErrorCode = result.ErrorCode;
        refund.NextCheckAt = Now + Config.StatusCheckDelay;
        Event(order.Id, "RefundSubmitted", $"{refund.State} {result.ErrorCode}".Trim());
    }

    public void RefundOutcome(Order order, Refund refund, HubtelResult result)
    {
        if (Terminal(refund.State)) return;
        switch (result.Outcome)
        {
            case HubtelOutcome.Succeeded:
                refund.State = TxnState.Succeeded;
                refund.CompletedAt = Now;
                refund.ErrorCode = null;
                refund.NextCheckAt = null;
                refund.ExternalReference = result.ExternalReference is { Length: > 0 } reference ? "hubtel:" + reference : "hubtel-order:" + order.HubtelOrderId;
                refund.Evidence = result.ExternalReference is null ? "Hubtel status check reports the payment as Refunded." : "Hubtel refund callback 0000.";
                order.Status = OrderStatus.Refunded;
                Event(order.Id, "RefundCompleted", refund.ExternalReference);
                return;
            case HubtelOutcome.Failed:
                NeedsFinance(order, refund, result.ErrorCode ?? "hubtel.refund_failed");
                return;
            default:
                refund.State = result.Outcome == HubtelOutcome.Pending ? TxnState.Pending : TxnState.Unknown;
                Recheck(order, refund, refund.SubmittedAt ?? refund.CreatedAt);
                return;
        }
    }

    public void NeedsFinance(Order order, Refund refund, string reason)
    {
        refund.State = TxnState.Failed;
        refund.ErrorCode = reason;
        refund.NextCheckAt = null;
        Event(order.Id, "RefundNeedsFinance", reason);
        logger.LogWarning("Refund {Refund} for order {Order} needs finance: {Reason}", refund.Id, order.Id, reason);
    }

    // ---- Float transfer ----

    public void TransferOutcome(FloatTransfer transfer, HubtelResult result)
    {
        if (Terminal(transfer.State)) return;
        switch (result.Outcome)
        {
            case HubtelOutcome.Succeeded:
                transfer.State = TxnState.Succeeded; transfer.ConfirmedAt = Now; transfer.ErrorCode = null; transfer.NextCheckAt = null;
                Event(null, "FloatTransferConfirmed", $"{transfer.ClientReference} {Money(transfer.Amount)}");
                return;
            case HubtelOutcome.Failed:
                transfer.State = TxnState.Failed; transfer.ErrorCode = result.ErrorCode ?? "hubtel.transfer_failed"; transfer.NextCheckAt = null;
                Event(null, "FloatTransferFailed", $"{transfer.ClientReference} {transfer.ErrorCode}");
                return;
            default:
                transfer.State = result.Outcome == HubtelOutcome.Pending ? TxnState.Pending : TxnState.Unknown;
                if (Now - transfer.CreatedAt >= TimeSpan.FromHours(Config.ReviewAfterHours))
                {
                    logger.LogError("Float transfer {Transfer} is still unresolved after a day", transfer.ClientReference);
                    transfer.NextCheckAt = Now.AddDays(1);
                }
                else transfer.NextCheckAt = Now.AddSeconds(BackoffSeconds[Math.Min(transfer.Checks, BackoffSeconds.Length - 1)]);
                return;
        }
    }

    // ---- Shared ----

    public void Review(Order order, string reason)
    {
        if (!order.NeedsReview) logger.LogWarning("Order {Order} needs review: {Reason}", order.Id, reason);
        order.NeedsReview = true;
        order.ReviewReason = reason;
        Event(order.Id, "ReviewRequired", reason);
    }

    public void Event(Guid? orderId, string kind, string detail = "", Guid? actor = null) =>
        db.OrderEvents.Add(new OrderEvent { OrderId = orderId, Kind = kind, Detail = detail, ActorId = actor });

    /// <summary>Backs off from 30 seconds to hourly; after a day unresolved, flags the order and checks daily.</summary>
    private void Recheck(Order order, IScheduled row, DateTimeOffset since)
    {
        if (Now - since >= TimeSpan.FromHours(Config.ReviewAfterHours))
        {
            if (order.ReviewReason != "unresolved_after_a_day") Review(order, "unresolved_after_a_day");
            row.NextCheckAt = Now.AddDays(1);
            return;
        }
        row.NextCheckAt = Now.AddSeconds(BackoffSeconds[Math.Min(row.Checks, BackoffSeconds.Length - 1)]);
    }

    private async Task<Refund?> RefundForAsync(Guid orderId, CancellationToken ct) =>
        db.Refunds.Local.SingleOrDefault(r => r.OrderId == orderId) ?? await db.Refunds.SingleOrDefaultAsync(r => r.OrderId == orderId, ct);

    private static string Money(decimal? amount) => amount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "";
}
