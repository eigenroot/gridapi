using System.Globalization;
using Grid.Api.Data;
using Grid.Api.Integrations.Hubtel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.BillPayments;

/// <summary>
/// The background half of bill payments. The tables are the queue: any order, bill, refund or
/// float transfer whose <c>next_check_at</c> is due gets claimed with a short lease, then sent (a
/// new bill or refund) or status-checked (anything unresolved). Hubtel is never called inside a
/// database transaction, and a row committed as Submitting is never sent again.
/// </summary>
public sealed class BillPaymentProcessor(GridDbContext db, HubtelPayments hubtel, OrderTransitions transitions, CallbackUrls callbacks,
    IOptions<BillPaymentOptions> options, TimeProvider time, ILogger<BillPaymentProcessor> logger)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private BillPaymentOptions Config => options.Value;
    private DateTimeOffset Now => time.GetUtcNow();

    /// <summary>Works one batch of due rows of each kind. Returns how many were handled.</summary>
    public async Task<int> RunDueAsync(int batchSize = 20, CancellationToken ct = default) =>
        await RunAsync(db.Orders, CheckPaymentAsync, batchSize, ct)
        + await RunAsync(db.Bills, ProcessBillAsync, batchSize, ct)
        + await RunAsync(db.Refunds, ProcessRefundAsync, batchSize, ct)
        + await RunAsync(db.FloatTransfers, CheckTransferAsync, batchSize, ct);

    private async Task<int> RunAsync<T>(DbSet<T> rows, Func<Guid, CancellationToken, Task> work, int batchSize, CancellationToken ct)
        where T : class, IScheduled
    {
        var now = Now;
        var due = await rows.AsNoTracking().Where(x => x.NextCheckAt <= now && (x.LeaseUntil == null || x.LeaseUntil < now))
            .OrderBy(x => x.NextCheckAt).Select(x => x.Id).Take(batchSize).ToListAsync(ct);
        var handled = 0;
        foreach (var id in due)
        {
            db.ChangeTracker.Clear();
            var leaseUntil = Now + Lease;
            var claimed = await rows.Where(x => x.Id == id && x.NextCheckAt <= now && (x.LeaseUntil == null || x.LeaseUntil < now))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, leaseUntil).SetProperty(x => x.Checks, x => x.Checks + 1), ct);
            if (claimed == 0) continue;
            handled++;
            try
            {
                await work(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // The row stays queued; anything already sent is only ever status-checked from here.
                logger.LogError(ex, "Bill payment work on {Kind} {Id} failed; retrying in a minute", typeof(T).Name, id);
                db.ChangeTracker.Clear();
                var retry = Now.AddMinutes(1);
                await rows.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextCheckAt, retry), CancellationToken.None);
            }
            finally
            {
                await rows.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), CancellationToken.None);
            }
        }
        return handled;
    }

    // ---- Payments: only ever status-checked; the prompt is sent when the order is placed ----

    private async Task CheckPaymentAsync(Guid id, CancellationToken ct)
    {
        var snapshot = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id, ct);
        if (OrderTransitions.Terminal(snapshot.PaymentState)) { await ClearAsync(db.Orders, id); return; }
        var result = await hubtel.GetStatusAsync(snapshot.ClientReference, ct);
        await InOrderAsync(id, async order =>
        {
            if (!OrderTransitions.Terminal(order.PaymentState)) await transitions.PaymentOutcomeAsync(order, result, ct);
        }, ct);
    }

    // ---- Bills ----

    private async Task ProcessBillAsync(Guid id, CancellationToken ct)
    {
        var snapshot = await db.Bills.AsNoTracking().SingleAsync(b => b.Id == id, ct);
        if (snapshot.State == TxnState.Created) { await SubmitBillAsync(snapshot, ct); return; }
        if (OrderTransitions.Terminal(snapshot.State)) { await ClearAsync(db.Bills, id); return; }
        var result = await hubtel.GetStatusAsync(snapshot.ClientReference, ct);
        await InOrderAsync(snapshot.OrderId, async order =>
        {
            var bill = await db.Bills.SingleAsync(b => b.Id == id, ct);
            if (bill.ClientReference == snapshot.ClientReference && !OrderTransitions.Terminal(bill.State))
                await transitions.BillOutcomeAsync(order, bill, result, ct);
        }, ct);
    }

    private async Task SubmitBillAsync(Bill snapshot, CancellationToken ct)
    {
        if (!Config.Enabled) { await PostponeAsync(db.Bills, snapshot.Id, TimeSpan.FromMinutes(1)); return; }
        Order? order = null;
        Bill? bill = null;
        await InOrderAsync(snapshot.OrderId, async locked =>
        {
            order = locked;
            bill = await db.Bills.SingleAsync(b => b.Id == snapshot.Id, ct);
            if (bill.State != TxnState.Created) { bill = null; return; }
            if (order.NeedsReview) { bill.NextCheckAt = Now.AddHours(1); bill = null; return; }
            if (order.PaymentState != TxnState.Succeeded || await db.Refunds.AnyAsync(r => r.OrderId == order.Id, ct))
            {
                transitions.Review(order, "bill_without_payment");
                bill.NextCheckAt = null; bill = null; return;
            }
            if (Now - (order.PaidAt ?? bill.CreatedAt) >= TimeSpan.FromHours(Config.ReviewAfterHours))
            {
                // Paid for but never sent within a day: safe to give the money back instead.
                await transitions.BillFailedAsync(order, bill, new HubtelResult(HubtelOutcome.Failed, ErrorCode: "bill.not_sent_in_time"), ct);
                bill = null; return;
            }
            bill.ClientReference = OrderTransitions.BillReference(bill);
            bill.Attempts++;
            bill.State = TxnState.Submitting;
            bill.SubmittedAt = Now;
            bill.Checks = 0;
            bill.NextCheckAt = Now + Config.StatusCheckDelay;
            transitions.Event(order.Id, "BillSubmitting", bill.ClientReference);
        }, ct);
        if (order is null || bill is null) return;

        var product = BillProducts.Require(order.ProductCode);
        var result = await hubtel.PayBillAsync(product, bill.ClientReference, order.Destination, bill.Amount, order.Bundle, order.SessionId,
            order.ReceiptEmail, callbacks.For(CallbackKind.Bill, bill.Id), CancellationToken.None);
        await InOrderAsync(order.Id, async locked =>
        {
            var current = await db.Bills.SingleAsync(b => b.Id == snapshot.Id, CancellationToken.None);
            await transitions.BillSubmittedAsync(locked, current, result, CancellationToken.None);
        }, CancellationToken.None);
        if (transitions.FloatSweepRequested) await CheckFloatAsync(ct);
    }

    // ---- Refunds ----

    private async Task ProcessRefundAsync(Guid id, CancellationToken ct)
    {
        var snapshot = await db.Refunds.AsNoTracking().SingleAsync(r => r.Id == id, ct);
        if (OrderTransitions.Terminal(snapshot.State)) { await ClearAsync(db.Refunds, id); return; }

        if (snapshot.State == TxnState.Created)
        {
            Order? order = null;
            var send = false;
            await InOrderAsync(snapshot.OrderId, async locked =>
            {
                order = locked;
                var refund = await db.Refunds.SingleAsync(r => r.Id == id, ct);
                if (refund.State != TxnState.Created) return;
                var bill = await db.Bills.AsNoTracking().SingleOrDefaultAsync(b => b.OrderId == locked.Id, ct);
                if (transitions.RefundBlocker(locked, refund, bill) is { } blocker) { transitions.NeedsFinance(locked, refund, blocker); return; }
                refund.State = TxnState.Submitting;
                refund.SubmittedAt = Now;
                refund.Checks = 0;
                refund.NextCheckAt = Now + Config.StatusCheckDelay;
                transitions.Event(locked.Id, "RefundSubmitting");
                send = true;
            }, ct);
            if (!send) return;

            var result = await hubtel.RefundAsync(order!.HubtelOrderId!, callbacks.For(CallbackKind.Refund, id), CancellationToken.None);
            await InOrderAsync(order.Id, async locked =>
                transitions.RefundSubmitted(locked, await db.Refunds.SingleAsync(r => r.Id == id, CancellationToken.None), result),
                CancellationToken.None);
            return;
        }

        // The Refund API has no status endpoint; the original payment's status turns "Refunded".
        var paymentReference = await db.Orders.Where(o => o.Id == snapshot.OrderId).Select(o => o.ClientReference).SingleAsync(ct);
        var status = await hubtel.GetStatusAsync(paymentReference, ct);
        var outcome = status.IsRefunded
            ? status with { Outcome = HubtelOutcome.Succeeded, ExternalReference = null }
            : status with { Outcome = HubtelOutcome.Pending };
        await InOrderAsync(snapshot.OrderId, async locked =>
            transitions.RefundOutcome(locked, await db.Refunds.SingleAsync(r => r.Id == id, ct), outcome), ct);
    }

    // ---- Float ----

    private async Task CheckTransferAsync(Guid id, CancellationToken ct)
    {
        var snapshot = await db.FloatTransfers.AsNoTracking().SingleAsync(t => t.Id == id, ct);
        if (OrderTransitions.Terminal(snapshot.State)) { await ClearAsync(db.FloatTransfers, id); return; }
        var result = await hubtel.GetTransferStatusAsync(snapshot.ClientReference, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await OrderLocks.AdvisoryAsync(db, "transfer:" + id, ct);
        transitions.TransferOutcome(await db.FloatTransfers.SingleAsync(t => t.Id == id, ct), result);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Tops the Disbursement float up from collections when it falls below the low watermark. Only
    /// one sweep is open at a time (advisory lock plus a partial unique index).
    /// </summary>
    public async Task CheckFloatAsync(CancellationToken ct)
    {
        var f = Config.Float;
        if (await db.FloatTransfers.AnyAsync(t => t.State != TxnState.Succeeded && t.State != TxnState.Failed, ct)) return;
        var balance = await hubtel.GetFloatBalanceAsync(ct);
        if (balance is null || balance >= f.LowWatermark) return;
        if (!f.AutoSweep) { logger.LogWarning("Hubtel float is low: {Balance} GHS", balance); return; }
        var available = await hubtel.GetCollectionBalanceAsync(ct);
        if (available is null) return;
        var amount = Math.Floor(Math.Min(f.Target - balance.Value, available.Value - f.CollectionReserve) * 100m) / 100m;
        if (amount < f.MinTransfer)
        {
            logger.LogWarning("Hubtel float is low ({Balance} GHS) and collections ({Available} GHS) can't cover a sweep", balance, available);
            return;
        }

        var transfer = new FloatTransfer { ClientReference = "", Amount = amount, State = TxnState.Submitting, NextCheckAt = Now + Config.StatusCheckDelay };
        transfer.ClientReference = "T" + transfer.Id.ToString("N");
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await OrderLocks.AdvisoryAsync(db, "float-sweep", ct);
            if (await db.FloatTransfers.AnyAsync(t => t.State != TxnState.Succeeded && t.State != TxnState.Failed, ct)) { await tx.CommitAsync(ct); return; }
            db.FloatTransfers.Add(transfer);
            transitions.Event(null, "FloatTransferSubmitting", $"{transfer.ClientReference} {amount.ToString("0.00", CultureInfo.InvariantCulture)}");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var result = await hubtel.TransferToFloatAsync(transfer.ClientReference, amount, callbacks.For(CallbackKind.Transfer, transfer.Id), CancellationToken.None);
        db.ChangeTracker.Clear();
        await using (var tx = await db.Database.BeginTransactionAsync(CancellationToken.None))
        {
            await OrderLocks.AdvisoryAsync(db, "transfer:" + transfer.Id, CancellationToken.None);
            var row = await db.FloatTransfers.SingleAsync(t => t.Id == transfer.Id, CancellationToken.None);
            if (row.State == TxnState.Submitting)
            {
                row.State = result.Outcome switch
                {
                    HubtelOutcome.Failed => TxnState.Failed,
                    HubtelOutcome.Unknown => TxnState.Unknown,
                    _ => TxnState.Pending,
                };
                row.ErrorCode = result.ErrorCode;
                if (row.State == TxnState.Failed) row.NextCheckAt = null;
            }
            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        }
        logger.LogInformation("Swept {Amount} GHS into the Hubtel float: {Outcome}", amount, result.Outcome);
    }

    // ---- Helpers ----

    /// <summary>Runs <paramref name="change"/> on the locked order in one transaction and saves.</summary>
    private async Task InOrderAsync(Guid orderId, Func<Order, Task> change, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await OrderLocks.LockAsync(db, orderId, ct) ?? throw new InvalidOperationException($"Order {orderId} not found.");
        await change(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static Task ClearAsync<T>(DbSet<T> rows, Guid id) where T : class, IScheduled =>
        rows.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextCheckAt, (DateTimeOffset?)null), CancellationToken.None);

    private Task PostponeAsync<T>(DbSet<T> rows, Guid id, TimeSpan by) where T : class, IScheduled
    {
        var next = Now + by;
        return rows.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextCheckAt, next), CancellationToken.None);
    }
}

/// <summary>Runs <see cref="BillPaymentProcessor"/> inside the API process, like the KYC worker.</summary>
public sealed class BillPaymentWorker(IServiceScopeFactory scopes, IOptions<BillPaymentOptions> options, ILogger<BillPaymentWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RunWorker)
        {
            logger.LogInformation("Bill payment worker not running (Payments:RunWorker is false)");
            return;
        }
        var nextFloatCheck = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<BillPaymentProcessor>();
                await processor.RunDueAsync(ct: stoppingToken);
                if (DateTimeOffset.UtcNow >= nextFloatCheck)
                {
                    await processor.CheckFloatAsync(stoppingToken);
                    nextFloatCheck = DateTimeOffset.UtcNow.AddMinutes(options.Value.Float.CheckMinutes);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Bill payment worker iteration failed"); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)), stoppingToken);
        }
    }
}
