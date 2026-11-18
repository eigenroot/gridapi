using Grid.Api.Data;

namespace Grid.Api.Features.Marketplace;

public enum RequestUrgency { Standard, SameDay }

/// <summary>
/// <c>AwaitingPayment</c> until the MoMo prompt is approved; nothing is offered to providers before.
/// <c>Unfilled</c> means every round of offers ran out, and the payment is refunded.
/// </summary>
public enum ServiceRequestStatus { AwaitingPayment, PaymentFailed, Searching, Assigned, InProgress, Completed, Cancelled, Unfilled }

/// <summary>
/// What a customer asked for. It keeps its own copy of the address and customer name, so editing
/// or archiving the saved home later never rewrites a booking.
/// </summary>
public sealed class ServiceRequest : Entity
{
    /// <summary>Counts up from a sequence; shown as <c>GR-1042</c>.</summary>
    public int Number { get; set; }
    public Guid CustomerConsumerId { get; set; }
    public Guid? PropertyId { get; set; }

    /// <summary>The top-level service (cleaning, laundry); providers are matched on this.</summary>
    public Guid ServiceCategoryId { get; set; }

    /// <summary>Unused since packages; kept so older rows still read.</summary>
    public Guid? SubServiceCategoryId { get; set; }

    /// <summary>What the customer pays, from GRID's package prices. Fixed when the request is made.</summary>
    public decimal Total { get; set; }

    /// <summary>"Book Ama again": offered to this provider alone first.</summary>
    public Guid? PreferredProviderAccountId { get; set; }

    /// <summary>Hash of the client's <c>Idempotency-Key</c>, so a retried booking is not booked twice.</summary>
    public string? IdempotencyKeyHash { get; set; }

    public required string Title { get; set; }

    /// <summary>The size of the job in words, e.g. "3 bedrooms · 2 bathrooms".</summary>
    public string? Scope { get; set; }

    /// <summary>Laundry: what is in the bag.</summary>
    public List<string> Items { get; set; } = [];
    public string? Notes { get; set; }

    /// <summary>Unused since packages carry their own quantities.</summary>
    public decimal Units { get; set; } = 1;

    public DateTimeOffset ScheduledAt { get; set; }
    public RequestUrgency Urgency { get; set; }

    // Snapshot of who and where, taken when the request was made.
    public required string CustomerName { get; set; }
    public required string Area { get; set; }
    public required string City { get; set; }
    public required string Address { get; set; }
    public string? GhanaPostGps { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public Guid? ServiceAreaId { get; set; }

    public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.Searching;

    /// <summary>How many rounds of offers have gone out.</summary>
    public int DispatchRound { get; set; }

    /// <summary>When the dispatcher should look at this request again; null when it shouldn't.</summary>
    public DateTimeOffset? NextDispatchAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    public string Reference => $"GR-{Number}";
}

/// <summary>One package on a request, priced when the request was made.</summary>
public sealed class ServiceRequestLine : Entity
{
    public Guid ServiceRequestId { get; set; }
    public Guid ServicePackageId { get; set; }

    /// <summary>"Standard clean · 2 bedrooms", copied so a later rename leaves the booking as it was.</summary>
    public required string Name { get; set; }
    public string? UnitLabel { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Total { get; set; }
    public int SortOrder { get; set; }
}

public enum OfferStatus { Pending, Accepted, Declined, Expired, Withdrawn }

/// <summary>
/// One provider's chance to take a request. Several go out per round; the first acceptance wins
/// and the rest are withdrawn. A provider is offered a given request once.
/// </summary>
public sealed class JobOffer : Entity
{
    public Guid ServiceRequestId { get; set; }
    public Guid ProviderAccountId { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    public int Round { get; set; }

    /// <summary>What the customer would pay this provider, from the provider's own prices.</summary>
    public decimal Price { get; set; }

    /// <summary>Straight-line distance from the provider's base to the job when the offer was made.</summary>
    public decimal? DistanceKm { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public string? DeclineReason { get; set; }
}

/// <summary>Cleaning happens in one visit; laundry is collected, processed and brought back.</summary>
public enum JobWorkflow { ScheduledService, PickupAndDelivery }

public enum JobStage
{
    Accepted,
    // Cleaning.
    OnTheWay, Arrived, InProgress,
    // Laundry.
    PickedUp, Processing, ReadyForDelivery, OutForDelivery,
    // Both: the provider is done and the customer hasn't confirmed yet.
    AwaitingConfirmation,
    Completed,
    Cancelled,
}

/// <summary>The accepted work. Money is fixed at acceptance: price, GRID's fee and what the provider earns.</summary>
public sealed class Job : Entity
{
    /// <summary>Counts up from a sequence; shown as <c>GJ-1024</c>.</summary>
    public int Number { get; set; }
    public Guid ServiceRequestId { get; set; }
    public Guid JobOfferId { get; set; }
    public Guid ProviderAccountId { get; set; }
    public Guid CustomerConsumerId { get; set; }
    public JobWorkflow Workflow { get; set; }
    public JobStage Stage { get; set; } = JobStage.Accepted;

    public decimal Price { get; set; }
    public decimal FeePercent { get; set; }
    public decimal Fee { get; set; }
    public decimal Net { get; set; }
    public decimal? DistanceKm { get; set; }

    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public DateTimeOffset? WorkCompletedAt { get; set; }

    /// <summary>When the job is confirmed for the customer if they haven't done it themselves.</summary>
    public DateTimeOffset? AutoConfirmAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    public string Code => $"GJ-{Number}";

    public bool IsActive => Stage is not (JobStage.AwaitingConfirmation or JobStage.Completed or JobStage.Cancelled);
}

/// <summary>Who moved a job to which stage, and when. Append-only.</summary>
public sealed class JobEvent : Entity
{
    public Guid JobId { get; set; }
    public JobStage Stage { get; set; }
    public Guid? ActorConsumerId { get; set; }
    public string? Note { get; set; }
}

/// <summary><c>Pending</c> until the customer confirms, then <c>Available</c> until a payout takes it.</summary>
public enum EarningStatus { Pending, Available, Paid, Reversed }

/// <summary>What one job pays the provider. One per job, created when the provider finishes.</summary>
public sealed class ProviderEarning : Entity
{
    public Guid ProviderAccountId { get; set; }
    public Guid JobId { get; set; }
    public decimal Gross { get; set; }
    public decimal Fee { get; set; }
    public decimal Net { get; set; }
    public EarningStatus Status { get; set; } = EarningStatus.Pending;
    public DateTimeOffset? AvailableAt { get; set; }
    public Guid? PayoutId { get; set; }
}

public enum PayoutStatus { Scheduled, Paid, Failed }

/// <summary>
/// Money GRID sent, or is about to send, to a provider. GRID Ops records these; the API does not
/// move the money itself.
/// </summary>
public sealed class ProviderPayout : Entity
{
    /// <summary>Counts up from a sequence; shown as <c>PO-2804</c>.</summary>
    public int Number { get; set; }
    public Guid ProviderAccountId { get; set; }
    public decimal Amount { get; set; }
    public PayoutStatus Status { get; set; } = PayoutStatus.Scheduled;

    /// <summary>Where it went, masked, e.g. "MTN Mobile Money •••• 0192".</summary>
    public required string Destination { get; set; }

    /// <summary>The transfer's reference at the bank or wallet, once it has one.</summary>
    public string? ExternalReference { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public Guid RecordedByConsumerId { get; set; }

    public string Reference => $"PO-{Number}";
}

/// <summary>The customer's rating of a finished job. One per job.</summary>
public sealed class Review : Entity
{
    public Guid JobId { get; set; }
    public Guid ServiceRequestId { get; set; }
    public Guid ProviderAccountId { get; set; }
    public Guid CustomerConsumerId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
}
