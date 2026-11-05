using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Integrations.Hubtel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.BillPayments;

public enum CallbackKind { Payment, Bill, Refund, Transfer }

/// <summary>
/// Per-row callback URLs. The last segment is an HMAC of the kind and row id, so the URL alone
/// proves which row Hubtel is reporting on.
/// </summary>
public sealed class CallbackUrls(IOptions<BillPaymentOptions> options)
{
    public string For(CallbackKind kind, Guid id) =>
        $"{options.Value.CallbackBaseUrl.TrimEnd('/')}/api/v1/hubtel/callbacks/{Segment(kind)}/{id}/{Sign(kind, id)}";

    public bool IsValid(CallbackKind kind, Guid id, string signature) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(kind, id)), Encoding.UTF8.GetBytes(signature));

    public static string Segment(CallbackKind kind) => kind.ToString().ToLowerInvariant();

    private string Sign(CallbackKind kind, Guid id) => Convert.ToHexStringLower(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(options.Value.Hubtel.CallbackSecret), Encoding.UTF8.GetBytes($"{Segment(kind)}:{id}")));
}

/// <summary>
/// Applies a Hubtel callback straight away, under the order's row lock, through
/// <see cref="OrderTransitions"/>. Repeats are harmless: a settled row ignores the same news.
/// </summary>
public sealed class HubtelCallbackHandler(GridDbContext db, OrderTransitions transitions, CallbackUrls urls,
    IOptions<BillPaymentOptions> options, ILogger<HubtelCallbackHandler> logger)
{
    public async Task HandleAsync(CallbackKind kind, Guid id, string signature, IPAddress? source, JsonElement body, CancellationToken ct)
    {
        if (!urls.IsValid(kind, id, signature)) throw Forbidden();
        CheckSource(kind, id, source);
        var result = HubtelPayments.InterpretCallback(body, out var reference);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        Guid? orderForEvent = null;
        switch (kind)
        {
            case CallbackKind.Payment:
            {
                var order = await OrderLocks.LockAsync(db, id, ct) ?? throw NotFound();
                RequireMatch(reference, order.ClientReference);
                await transitions.PaymentOutcomeAsync(order, result, ct);
                orderForEvent = order.Id;
                break;
            }
            case CallbackKind.Bill:
            {
                var orderId = await db.Bills.Where(b => b.Id == id).Select(b => (Guid?)b.OrderId).SingleOrDefaultAsync(ct) ?? throw NotFound();
                var order = await OrderLocks.LockAsync(db, orderId, ct) ?? throw NotFound();
                var bill = await db.Bills.SingleAsync(b => b.Id == id, ct);
                RequireMatch(reference, bill.ClientReference);
                await transitions.BillOutcomeAsync(order, bill, result, ct);
                orderForEvent = order.Id;
                break;
            }
            case CallbackKind.Refund:
            {
                var orderId = await db.Refunds.Where(r => r.Id == id).Select(r => (Guid?)r.OrderId).SingleOrDefaultAsync(ct) ?? throw NotFound();
                var order = await OrderLocks.LockAsync(db, orderId, ct) ?? throw NotFound();
                var refund = await db.Refunds.SingleAsync(r => r.Id == id, ct);
                // Refund callbacks carry Hubtel's order id rather than our reference.
                RequireMatch(result.OrderId, order.HubtelOrderId);
                transitions.RefundOutcome(order, refund, result);
                orderForEvent = order.Id;
                break;
            }
            case CallbackKind.Transfer:
            {
                await OrderLocks.AdvisoryAsync(db, "transfer:" + id, ct);
                var transfer = await db.FloatTransfers.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw NotFound();
                RequireMatch(reference, transfer.ClientReference);
                transitions.TransferOutcome(transfer, result);
                break;
            }
        }
        transitions.Event(orderForEvent, "CallbackReceived",
            $"{CallbackUrls.Segment(kind)} {result.Outcome} {result.ErrorCode}".Trim());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private void CheckSource(CallbackKind kind, Guid id, IPAddress? source)
    {
        var hubtel = options.Value.Hubtel;
        if (source is { IsIPv4MappedToIPv6: true }) source = source.MapToIPv4();
        var allowed = hubtel.CallbackIps.Count > 0 ? hubtel.CallbackIps : [.. HubtelPaymentOptions.DocumentedCallbackIps];
        if (source is not null && allowed.Any(ip => IPAddress.Parse(ip).Equals(source))) return;
        if (hubtel.EnforceCallbackIp) throw Forbidden();
        logger.LogWarning("Hubtel {Kind} callback for {Id} came from unlisted address {Source}", kind, id, source);
    }

    private static void RequireMatch(string? reported, string? expected)
    {
        if (reported is not null && reported != expected)
            throw new ApiException(400, "payments.callback_mismatch", "The callback does not match this payment.");
    }

    private static ApiException Forbidden() => new(403, "payments.callback_forbidden", "Callback authentication failed.");
    private static ApiException NotFound() => Errors.NotFound("payments.callback_unknown", "That payment was not found.");
}

/// <summary>Row locks that serialise every change to one order.</summary>
public static class OrderLocks
{
    public static Task<Order?> LockAsync(GridDbContext db, Guid orderId, CancellationToken ct) =>
        db.Orders.FromSqlInterpolated($"SELECT * FROM orders WHERE id = {orderId} FOR UPDATE").SingleOrDefaultAsync(ct);

    public static Task AdvisoryAsync(GridDbContext db, string key, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
}
