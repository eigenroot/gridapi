using Grid.Api.Data;
using Grid.Api.Features.BillPayments;

namespace Grid.Api.Features.ServicePayments;

/// <summary>
/// How service payments are taken. <c>Hubtel</c> prompts the customer's wallet. <c>Simulated</c>
/// treats every booking as paid at once, for development and tests; production refuses it.
/// </summary>
public enum ServicePaymentMode { Hubtel, Simulated }

/// <summary>
/// One attempt to collect a booking's total by MoMo, and its refund if the booking falls through.
/// GRID holds the money: providers are paid from earnings, never from this row.
/// </summary>
public sealed class ServicePayment : Entity
{
    public Guid ServiceRequestId { get; set; }
    public Guid ConsumerId { get; set; }
    public decimal Amount { get; set; }
    public required string Channel { get; set; }
    public required string PayerPhone { get; set; }

    /// <summary>Our reference at Hubtel, <c>S{id}</c>.</summary>
    public required string ClientReference { get; set; }

    public TxnState State { get; set; } = TxnState.Created;
    public string? Error { get; set; }
    public string? HubtelTransactionId { get; set; }
    public string? HubtelOrderId { get; set; }
    public decimal? Charges { get; set; }
    public decimal? AmountPaid { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>When to ask Hubtel again about a payment or refund still open.</summary>
    public DateTimeOffset? NextCheckAt { get; set; }
    public int Checks { get; set; }

    /// <summary>Null until a refund is started.</summary>
    public TxnState? RefundState { get; set; }
    public string? RefundReason { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }

    public bool IsPaid => State == TxnState.Succeeded;
}
