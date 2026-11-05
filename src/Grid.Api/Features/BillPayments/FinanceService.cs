using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Consumers;
using Grid.Api.Integrations.Hubtel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.BillPayments;

/// <summary>The finance side, for the phone numbers in <c>Payments:FinanceOperators</c>.</summary>
public sealed class FinanceService(GridDbContext db, CurrentConsumer current, OrderTransitions transitions, HubtelPayments hubtel,
    IOptions<BillPaymentOptions> options, TimeProvider time)
{
    private BillPaymentOptions Config => options.Value;
    private DateTimeOffset Now => time.GetUtcNow();

    public async Task<FinanceSummary> SummaryAsync(CancellationToken ct)
    {
        await AuthorizeAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var collected = await db.Orders.Where(o => o.PaymentState == TxnState.Succeeded).SumAsync(o => o.AmountPaid ?? 0, ct);
        var delivered = await db.Bills.Where(b => b.State == TxnState.Succeeded).SumAsync(b => b.Amount, ct);
        var open = await db.Refunds.Where(r => r.State != TxnState.Succeeded).SumAsync(r => r.Amount, ct);
        var refunded = await db.Refunds.Where(r => r.State == TxnState.Succeeded).SumAsync(r => r.Amount, ct);
        var review = await db.Orders.CountAsync(o => o.NeedsReview, ct);
        var payments = await db.Orders.CountAsync(o => o.PaymentState == TxnState.Submitting || o.PaymentState == TxnState.Pending || o.PaymentState == TxnState.Unknown, ct);
        var bills = await db.Bills.CountAsync(b => b.State != TxnState.Succeeded && b.State != TxnState.Failed, ct);
        var late = Now.AddMinutes(-5);
        var overdue = await db.Orders.CountAsync(o => o.NextCheckAt < late, ct) + await db.Bills.CountAsync(b => b.NextCheckAt < late, ct)
            + await db.Refunds.CountAsync(r => r.NextCheckAt < late, ct) + await db.FloatTransfers.CountAsync(t => t.NextCheckAt < late, ct);
        await tx.CommitAsync(ct);
        return new(MoneyDto.From(collected), MoneyDto.From(delivered), MoneyDto.From(open), MoneyDto.From(refunded), review, payments, bills, overdue);
    }

    public async Task<FloatResponse> FloatAsync(CancellationToken ct)
    {
        await AuthorizeAsync(ct);
        var collection = await hubtel.GetCollectionBalanceAsync(ct);
        var balance = await hubtel.GetFloatBalanceAsync(ct);
        var open = await db.FloatTransfers.AsNoTracking().Where(t => t.State != TxnState.Succeeded && t.State != TxnState.Failed)
            .OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        var last = await db.FloatTransfers.AsNoTracking().Where(t => t.State == TxnState.Succeeded || t.State == TxnState.Failed)
            .OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        return new(MoneyDto.From(collection), MoneyDto.From(balance), MoneyDto.From(Config.Float.LowWatermark), MoneyDto.From(Config.Float.Target),
            Config.Float.AutoSweep, open is null ? null : FloatTransferSummary.From(open), last is null ? null : FloatTransferSummary.From(last));
    }

    public async Task<IReadOnlyList<OrderResponse>> OrdersAsync(OrderStatus? status, bool? needsReview, int limit, CancellationToken ct)
    {
        await AuthorizeAsync(ct);
        if (limit is < 1 or > 100) throw Errors.Validation([new("limit", "Use 1–100.")]);
        var query = db.Orders.AsNoTracking();
        if (status is { } s) query = query.Where(o => o.Status == s);
        if (needsReview is { } r) query = query.Where(o => o.NeedsReview == r);
        var rows = await query.OrderByDescending(o => o.CreatedAt).Take(limit).ToListAsync(ct);
        var ids = rows.Select(o => o.Id).ToList();
        var bills = await db.Bills.AsNoTracking().Where(b => ids.Contains(b.OrderId)).ToDictionaryAsync(b => b.OrderId, ct);
        var refunds = await db.Refunds.AsNoTracking().Where(x => ids.Contains(x.OrderId)).ToDictionaryAsync(x => x.OrderId, ct);
        return rows.Select(o => OrderViews.Describe(o, bills.GetValueOrDefault(o.Id), refunds.GetValueOrDefault(o.Id))).ToList();
    }

    public async Task<FinanceOrderResponse> OrderAsync(Guid id, CancellationToken ct)
    {
        await AuthorizeAsync(ct);
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw OrderMissing();
        var bill = await db.Bills.AsNoTracking().SingleOrDefaultAsync(b => b.OrderId == id, ct);
        var refund = await db.Refunds.AsNoTracking().SingleOrDefaultAsync(r => r.OrderId == id, ct);
        var events = await db.OrderEvents.AsNoTracking().Where(e => e.OrderId == id).OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new EventResponse(e.Kind, e.Detail, e.ActorId, e.CreatedAt)).ToListAsync(ct);
        return new(OrderViews.Describe(order, bill, refund), order.PayerPhone, order.ClientReference, order.HubtelTransactionId,
            order.HubtelOrderId, order.ReviewReason, bill is null ? null : OrderViews.Detail(bill),
            refund is null ? null : FinanceRefundResponse.From(refund), events);
    }

    /// <summary>Clears the review flag once someone has looked. It never changes money outcomes.</summary>
    public async Task<OrderResponse> ResolveReviewAsync(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await AuthorizeAsync(ct);
        var reason = Required(request.Reason, "reason", 500);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await OrderLocks.LockAsync(db, id, ct) ?? throw OrderMissing();
        if (order.NeedsReview)
        {
            transitions.Event(order.Id, "ReviewResolved", $"{order.ReviewReason}: {reason}", current.Id);
            order.NeedsReview = false;
            order.ReviewReason = null;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return OrderViews.Describe(order, await db.Bills.AsNoTracking().SingleOrDefaultAsync(b => b.OrderId == id, ct),
            await db.Refunds.AsNoTracking().SingleOrDefaultAsync(r => r.OrderId == id, ct));
    }

    public async Task<IReadOnlyList<FinanceRefundResponse>> RefundsAsync(TxnState? state, CancellationToken ct)
    {
        await AuthorizeAsync(ct);
        var query = db.Refunds.AsNoTracking();
        if (state is { } s) query = query.Where(r => r.State == s);
        return (await query.OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync(ct)).Select(FinanceRefundResponse.From).ToList();
    }

    /// <summary>Sends a refund that fell to finance through Hubtel's Refund API again.</summary>
    public async Task<FinanceRefundResponse> RetryRefundAsync(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await AuthorizeAsync(ct);
        var reason = Required(request.Reason, "reason", 500);
        return await OnRefundAsync(id, async (order, refund) =>
        {
            if (refund.State != TxnState.Failed)
                throw Errors.Conflict("finance.refund_not_with_finance", "Only a refund waiting for finance can be retried.");
            var bill = await db.Bills.AsNoTracking().SingleOrDefaultAsync(b => b.OrderId == order.Id, ct);
            if (transitions.RefundBlocker(order, refund, bill) is { } blocker)
                throw Errors.Conflict("finance.refund_not_eligible", $"Hubtel can't refund this order ({blocker}). Return it by hand.");
            refund.State = TxnState.Created; refund.ErrorCode = null; refund.Checks = 0; refund.NextCheckAt = Now;
            order.Status = OrderStatus.Refunding;
            transitions.Event(order.Id, "RefundRetried", reason, current.Id);
        }, ct);
    }

    /// <summary>Records money returned outside Hubtel's Refund API.</summary>
    public async Task<FinanceRefundResponse> CompleteRefundAsync(Guid id, CompleteRefundRequest request, CancellationToken ct)
    {
        await AuthorizeAsync(ct);
        var reason = Required(request.Reason, "reason", 500);
        var reference = Required(request.ExternalReference, "externalReference", 100);
        var evidence = Required(request.Evidence, "evidence", 1000);
        return await OnRefundAsync(id, async (order, refund) =>
        {
            if (refund.State == TxnState.Succeeded)
            {
                if (refund.ExternalReference != reference) throw Errors.Conflict("finance.refund_already_completed", "This refund is already completed.");
                return;
            }
            if (refund.State != TxnState.Failed)
                throw Errors.Conflict("finance.refund_in_progress", "Hubtel is still returning this money.");
            var bill = await db.Bills.AsNoTracking().SingleOrDefaultAsync(b => b.OrderId == order.Id, ct);
            if (order.NeedsReview || (bill is not null && bill.State != TxnState.Failed))
                throw Errors.Conflict("finance.refund_unresolved", "Resolve the order's review and its bill payment before returning money.");
            if (await db.Refunds.AnyAsync(r => r.Id != refund.Id && r.ExternalReference == reference, ct))
                throw Errors.Conflict("finance.refund_reference_used", "That external reference is already recorded.");
            refund.State = TxnState.Succeeded; refund.Method = RefundMethod.Manual; refund.ExternalReference = reference;
            refund.Evidence = evidence; refund.CompletedBy = current.Id; refund.CompletedAt = Now; refund.ErrorCode = null;
            order.Status = OrderStatus.Refunded;
            transitions.Event(order.Id, "RefundCompletedManually", $"{reference}: {reason}", current.Id);
        }, ct);
    }

    private async Task<FinanceRefundResponse> OnRefundAsync(Guid refundId, Func<Order, Refund, Task> change, CancellationToken ct)
    {
        var orderId = await db.Refunds.Where(r => r.Id == refundId).Select(r => (Guid?)r.OrderId).SingleOrDefaultAsync(ct)
            ?? throw Errors.NotFound("finance.refund_not_found", "That refund was not found.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = (await OrderLocks.LockAsync(db, orderId, ct))!;
        var refund = await db.Refunds.SingleAsync(r => r.Id == refundId, ct);
        await change(order, refund);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return FinanceRefundResponse.From(refund);
    }

    private async Task AuthorizeAsync(CancellationToken ct)
    {
        var consumer = await current.LoadAsync(ct);
        var operators = Config.FinanceOperators.Select(p => PhoneNumbers.TryNormalize(p, out var n) ? n : null);
        if (!operators.Contains(consumer.PhoneNumber)) throw new ApiException(403, "finance.forbidden", "Finance access is required.");
    }

    private static string Required(string? value, string field, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim()
            : throw Errors.Validation([new(field, $"Enter {field} (1–{max} characters).")]);

    private static ApiException OrderMissing() => Errors.NotFound("orders.not_found", "That order was not found.");
}
