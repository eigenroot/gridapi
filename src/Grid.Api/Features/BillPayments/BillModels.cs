using Grid.Api.Data;

namespace Grid.Api.Features.BillPayments;

/// <summary>
/// The state of one call to Hubtel that moves money. <see cref="Submitting"/> is committed before
/// the call goes out; a row found in it later is only ever status-checked, never sent again.
/// </summary>
public enum TxnState { Created, Submitting, Pending, Succeeded, Failed, Unknown }

/// <summary>What the customer sees.</summary>
public enum OrderStatus { AwaitingPayment, PaymentFailed, Processing, Completed, Refunding, Refunded }

public enum RefundMethod { Hubtel, Manual }

/// <summary>A row the payment worker revisits: due at <see cref="NextCheckAt"/>, claimed until <see cref="LeaseUntil"/>.</summary>
public interface IScheduled
{
    Guid Id { get; }
    DateTimeOffset? NextCheckAt { get; set; }
    DateTimeOffset? LeaseUntil { get; set; }
    int Checks { get; set; }
}

/// <summary>
/// One purchase and its single mobile-money payment. A declined payment ends the order
/// (<see cref="OrderStatus.PaymentFailed"/>); the customer simply places a new one.
/// </summary>
public sealed class Order : Entity, IScheduled
{
    public Guid ConsumerId { get; set; }
    public Guid? SavedAccountId { get; set; }
    public Guid? PropertyId { get; set; }

    // What is being bought. Destination and Bundle are exactly what Hubtel's pay call takes.
    public required string ProductCode { get; set; }
    public required string AccountNumber { get; set; }
    public required string Destination { get; set; }
    public string? Bundle { get; set; }
    /// <summary>Ghana Water's one-time session from the lookup made when the order was placed.</summary>
    public string? SessionId { get; set; }
    public string? ReceiptEmail { get; set; }
    public string CustomerName { get; set; } = "";
    public bool NameConfirmed { get; set; }

    // Frozen at creation. Total is what Hubtel collects; it adds its own charge on top.
    public decimal Principal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }

    // The mobile-money payment.
    public required string ClientReference { get; set; }
    public required string Channel { get; set; }
    public required string PayerPhone { get; set; }
    public TxnState PaymentState { get; set; }
    public string? HubtelTransactionId { get; set; }
    /// <summary>Hubtel's order id for the payment; the Refund API is keyed on it.</summary>
    public string? HubtelOrderId { get; set; }
    public decimal? Charges { get; set; }
    public decimal? AmountCharged { get; set; }
    public decimal? AmountPaid { get; set; }
    public string? PaymentError { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    public OrderStatus Status { get; set; }
    public bool NeedsReview { get; set; }
    public string? ReviewReason { get; set; }

    public required string IdempotencyKeyHash { get; set; }
    public required string RequestHash { get; set; }

    public DateTimeOffset? NextCheckAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public int Checks { get; set; }
}

/// <summary>
/// Paying the biller from the float; at most one per order. A retry after a rejection Hubtel made
/// before processing gets a fresh <see cref="ClientReference"/> on the same row.
/// </summary>
public sealed class Bill : Entity, IScheduled
{
    public Guid OrderId { get; set; }
    public required string ClientReference { get; set; }
    public decimal Amount { get; set; }
    public TxnState State { get; set; }
    public int Attempts { get; set; }
    public string? HubtelTransactionId { get; set; }
    /// <summary>The biller's or telco's own reference, shown on the receipt.</summary>
    public string? ExternalReference { get; set; }
    public decimal? Commission { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }

    public DateTimeOffset? NextCheckAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public int Checks { get; set; }
}

/// <summary>
/// Returning a paid order's money; at most one per order. <see cref="TxnState.Failed"/> means it
/// needs finance (Hubtel refused or it wasn't eligible); <see cref="TxnState.Succeeded"/> means the
/// money is back with the customer.
/// </summary>
public sealed class Refund : Entity, IScheduled
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public required string Reason { get; set; }
    public TxnState State { get; set; }
    public RefundMethod Method { get; set; }
    public string? ErrorCode { get; set; }
    public string? ExternalReference { get; set; }
    public string? Evidence { get; set; }
    public Guid? CompletedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? NextCheckAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public int Checks { get; set; }
}

/// <summary>A sweep from the Collection account into the Disbursement float that pays bills.</summary>
public sealed class FloatTransfer : Entity, IScheduled
{
    public required string ClientReference { get; set; }
    public decimal Amount { get; set; }
    public TxnState State { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset? NextCheckAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public int Checks { get; set; }
}

/// <summary>Append-only history. Detail holds codes and references, never raw provider payloads.</summary>
public sealed class OrderEvent : Entity
{
    public Guid? OrderId { get; set; }
    public required string Kind { get; set; }
    public string Detail { get; set; } = "";
    public Guid? ActorId { get; set; }
}
