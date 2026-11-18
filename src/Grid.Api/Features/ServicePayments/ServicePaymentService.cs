using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.BillPayments;
using Grid.Api.Features.Consumers;
using Grid.Api.Features.Marketplace;
using Grid.Api.Integrations.Hubtel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.ServicePayments;

/// <summary>What the customer sees of a booking's payment.</summary>
public enum ServicePaymentStatus { AwaitingApproval, Paid, Failed, Refunding, Refunded, RefundFailed }

public sealed record ServicePaymentResponse(
    ServicePaymentStatus Status,
    decimal Amount,
    string Channel,
    string PayerPhone,
    string? Error,
    DateTimeOffset? PaidAt,
    DateTimeOffset? RefundedAt,
    string? NextAction)
{
    public static ServicePaymentResponse From(ServicePayment p)
    {
        var status = p.RefundState switch
        {
            TxnState.Succeeded => ServicePaymentStatus.Refunded,
            TxnState.Failed => ServicePaymentStatus.RefundFailed,
            not null => ServicePaymentStatus.Refunding,
            null => p.State switch
            {
                TxnState.Succeeded => ServicePaymentStatus.Paid,
                TxnState.Failed => ServicePaymentStatus.Failed,
                _ => ServicePaymentStatus.AwaitingApproval,
            },
        };
        return new(status, p.Amount, p.Channel, p.PayerPhone, p.Error, p.PaidAt, p.RefundedAt,
            status == ServicePaymentStatus.AwaitingApproval ? "ApproveOnPhone" : null);
    }
}

/// <summary>
/// Collects a booking's total by MoMo before anyone is offered the job, and refunds it when the
/// booking falls through. Hubtel is only ever called outside a database transaction: the row is
/// saved first, and an unclear answer is settled later by a status check, never by a second prompt.
/// </summary>
public sealed class ServicePaymentService(
    GridDbContext db,
    HubtelPayments hubtel,
    CallbackUrls callbacks,
    IOptions<MarketplaceOptions> options,
    TimeProvider time,
    ILogger<ServicePaymentService> logger)
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    private MarketplaceOptions Config => options.Value;
    private DateTimeOffset Now => time.GetUtcNow();

    /// <summary>
    /// Prompts the customer's wallet for a request that is <c>AwaitingPayment</c>. The request must
    /// already be saved. Returns once Hubtel has answered the prompt (not once it is approved).
    /// </summary>
    public async Task StartAsync(ServiceRequest request, Consumer consumer, string channel, CancellationToken ct)
    {
        var payment = new ServicePayment
        {
            ServiceRequestId = request.Id,
            ConsumerId = consumer.Id,
            Amount = request.Total,
            Channel = channel,
            PayerPhone = consumer.PhoneNumber,
            ClientReference = "",
            State = TxnState.Submitting,
        };
        payment.ClientReference = "S" + payment.Id.ToString("N");
        db.ServicePayments.Add(payment);

        if (Config.PaymentMode == ServicePaymentMode.Simulated)
        {
            Paid(payment, request, new HubtelResult(HubtelOutcome.Succeeded, Amount: payment.Amount, TransactionId: "sim-" + payment.ClientReference));
            await db.SaveChangesAsync(ct);
            return;
        }

        await db.SaveChangesAsync(ct);
        var result = await hubtel.ReceiveMoneyAsync(payment.ClientReference, payment.PayerPhone, payment.Channel, payment.Amount,
            $"GRID {request.Title}", NullIfBlank(consumer.FullName), NullIfBlank(consumer.Email),
            callbacks.For(CallbackKind.ServicePayment, payment.Id), CancellationToken.None);

        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);
        payment = (await LockAsync(payment.Id, CancellationToken.None))!;
        payment.HubtelTransactionId ??= result.TransactionId;
        payment.HubtelOrderId ??= result.OrderId;
        payment.Charges ??= result.Charges;
        if (payment.State == TxnState.Submitting)
        {
            if (result.Outcome == HubtelOutcome.Failed)
            {
                await FailedAsync(payment, result.ErrorCode, CancellationToken.None);
            }
            else
            {
                payment.State = result.Outcome == HubtelOutcome.Unknown ? TxnState.Unknown : TxnState.Pending;
                payment.NextCheckAt = Now + Backoff[0];
            }
        }
        await db.SaveChangesAsync(CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);
    }

    /// <summary>A payment outcome from a callback or a status check. The caller holds the row lock and saves.</summary>
    public async Task OutcomeAsync(ServicePayment payment, HubtelResult result, CancellationToken ct)
    {
        if (result.Outcome == HubtelOutcome.Pending && result.IsUnpaid && Now - payment.CreatedAt >= TimeSpan.FromMinutes(Config.PaymentTimeoutMinutes))
            result = result with { Outcome = HubtelOutcome.Failed, ErrorCode = "hubtel.unpaid" };

        switch (result.Outcome)
        {
            case HubtelOutcome.Succeeded when payment.State == TxnState.Succeeded:
                return;
            case HubtelOutcome.Succeeded:
            {
                var request = await db.ServiceRequests.SingleAsync(r => r.Id == payment.ServiceRequestId, ct);
                Paid(payment, request, result);
                return;
            }
            case HubtelOutcome.Failed:
                if (payment.State is not (TxnState.Succeeded or TxnState.Failed)) await FailedAsync(payment, result.ErrorCode, ct);
                return;
            default:
                if (payment.State is TxnState.Succeeded or TxnState.Failed) return;
                payment.State = result.Outcome == HubtelOutcome.Pending ? TxnState.Pending : TxnState.Unknown;
                if (Now - payment.CreatedAt >= TimeSpan.FromMinutes(Config.PaymentTimeoutMinutes) && result.Outcome == HubtelOutcome.Pending)
                {
                    // Hubtel still says pending well past the prompt's life; keep checking, but slowly.
                    payment.NextCheckAt = Now + TimeSpan.FromMinutes(15);
                }
                else
                {
                    payment.NextCheckAt = Now + Backoff[Math.Min(payment.Checks, Backoff.Length - 1)];
                }
                payment.Checks++;
                return;
        }
    }

    /// <summary>A refund outcome from a callback or a status check. The caller holds the row lock and saves.</summary>
    public void RefundOutcome(ServicePayment payment, HubtelResult result)
    {
        if (payment.RefundState is TxnState.Succeeded or TxnState.Failed or null) return;
        switch (result.Outcome)
        {
            case HubtelOutcome.Succeeded:
                payment.RefundState = TxnState.Succeeded;
                payment.RefundedAt = Now;
                payment.NextCheckAt = null;
                break;
            case HubtelOutcome.Failed:
                payment.RefundState = TxnState.Failed;
                payment.Error = result.ErrorCode ?? "hubtel.refund_failed";
                payment.NextCheckAt = null;
                logger.LogWarning("Refund of service payment {Reference} failed: {Error}", payment.ClientReference, payment.Error);
                break;
            default:
                payment.RefundState = TxnState.Pending;
                payment.NextCheckAt = Now + Backoff[Math.Min(payment.Checks++, Backoff.Length - 1)];
                break;
        }
    }

    /// <summary>
    /// Marks a request's payment for refund; the worker sends it. Safe to call more than once and
    /// when nothing was paid. The caller saves.
    /// </summary>
    public async Task RequestRefundAsync(Guid requestId, string reason, CancellationToken ct)
    {
        var payment = await db.ServicePayments
            .Where(p => p.ServiceRequestId == requestId && p.State == TxnState.Succeeded && p.RefundState == null)
            .SingleOrDefaultAsync(ct);
        if (payment is null) return;
        payment.RefundReason = reason;
        if (Config.PaymentMode == ServicePaymentMode.Simulated)
        {
            payment.RefundState = TxnState.Succeeded;
            payment.RefundedAt = Now;
            return;
        }
        payment.RefundState = TxnState.Created;
        payment.NextCheckAt = Now;
    }

    /// <summary>Settles open payments and sends or checks refunds. Returns how many rows it touched.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        if (Config.PaymentMode == ServicePaymentMode.Simulated) return 0;
        var due = await db.ServicePayments
            .Where(p => p.NextCheckAt != null && p.NextCheckAt <= Now)
            .OrderBy(p => p.NextCheckAt).Select(p => p.Id).Take(50).ToListAsync(ct);

        foreach (var id in due)
        {
            var snapshot = await db.ServicePayments.AsNoTracking().SingleAsync(p => p.Id == id, ct);
            HubtelResult result;
            var refunding = snapshot.RefundState is not null;
            try
            {
                result = snapshot.RefundState == TxnState.Created
                    ? await hubtel.RefundAsync(snapshot.HubtelOrderId ?? snapshot.HubtelTransactionId ?? snapshot.ClientReference,
                        callbacks.For(CallbackKind.ServiceRefund, snapshot.Id), ct)
                    : await hubtel.GetStatusAsync(snapshot.ClientReference, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Hubtel did not answer about service payment {Reference}", snapshot.ClientReference);
                result = new HubtelResult(HubtelOutcome.Unknown);
            }

            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var payment = await LockAsync(id, ct);
            if (payment is null) continue;
            if (refunding)
            {
                // A status check reports the original payment; "Refunded" there means the refund landed.
                if (payment.RefundState == TxnState.Pending && result.IsRefunded) result = result with { Outcome = HubtelOutcome.Succeeded };
                else if (payment.RefundState == TxnState.Pending && result.Outcome == HubtelOutcome.Succeeded) result = result with { Outcome = HubtelOutcome.Pending };
                RefundOutcome(payment, result);
            }
            else
            {
                await OutcomeAsync(payment, result, ct);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return due.Count;
    }

    public Task<ServicePayment?> LockAsync(Guid id, CancellationToken ct) =>
        db.ServicePayments.FromSqlInterpolated($"SELECT * FROM service_payments WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);

    /// <summary>The latest payment attempt for each request.</summary>
    public static async Task<Dictionary<Guid, ServicePayment>> LatestAsync(GridDbContext db, IReadOnlyCollection<Guid> requestIds, CancellationToken ct) =>
        (await db.ServicePayments.AsNoTracking().Where(p => requestIds.Contains(p.ServiceRequestId)).ToListAsync(ct))
        .GroupBy(p => p.ServiceRequestId)
        .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First());

    private void Paid(ServicePayment payment, ServiceRequest request, HubtelResult result)
    {
        var wasFailed = payment.State == TxnState.Failed;
        payment.State = TxnState.Succeeded;
        payment.PaidAt = Now;
        payment.AmountPaid = result.Amount;
        payment.HubtelTransactionId = result.TransactionId ?? payment.HubtelTransactionId;
        payment.HubtelOrderId ??= result.OrderId ?? result.TransactionId;
        payment.Charges ??= result.Charges;
        payment.Error = null;
        payment.NextCheckAt = null;

        if (request.Status == ServiceRequestStatus.AwaitingPayment && !wasFailed)
        {
            request.Status = ServiceRequestStatus.Searching;
            request.NextDispatchAt = Now;
            return;
        }

        // Paid after the booking moved on (cancelled, timed out, or paid again): the money goes back.
        payment.RefundReason = "LatePayment";
        payment.RefundState = Config.PaymentMode == ServicePaymentMode.Simulated ? TxnState.Succeeded : TxnState.Created;
        payment.NextCheckAt = payment.RefundState == TxnState.Created ? Now : null;
        logger.LogWarning("Service payment {Reference} succeeded after request {Request} was {Status}; refunding",
            payment.ClientReference, request.Reference, request.Status);
    }

    private async Task FailedAsync(ServicePayment payment, string? code, CancellationToken ct)
    {
        payment.State = TxnState.Failed;
        payment.Error = code ?? "hubtel.declined";
        payment.NextCheckAt = null;
        var request = await db.ServiceRequests.SingleAsync(r => r.Id == payment.ServiceRequestId, ct);
        if (request.Status == ServiceRequestStatus.AwaitingPayment)
        {
            request.Status = ServiceRequestStatus.PaymentFailed;
            request.NextDispatchAt = null;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
