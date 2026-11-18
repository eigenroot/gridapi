using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Marketplace;

/// <summary>
/// Every change to an offer or a job goes through here, so the rules live in one place: who may
/// do what, from which stage, and what happens to the request and the money as a result.
/// </summary>
public sealed class JobService(
    GridDbContext db,
    Dispatcher dispatcher,
    ServicePayments.ServicePaymentService payments,
    IOptions<MarketplaceOptions> options,
    TimeProvider time)
{
    private const string OneLiveJobPerRequestIndex = "ix_jobs_one_live_per_request";

    /// <summary>The provider's command at each stage, and the stage it leads to.</summary>
    private static readonly Dictionary<(JobWorkflow, JobStage), (string Command, JobStage Next)> Steps = new()
    {
        [(JobWorkflow.ScheduledService, JobStage.Accepted)] = ("start-journey", JobStage.OnTheWay),
        [(JobWorkflow.ScheduledService, JobStage.OnTheWay)] = ("arrive", JobStage.Arrived),
        [(JobWorkflow.ScheduledService, JobStage.Arrived)] = ("start-work", JobStage.InProgress),
        [(JobWorkflow.ScheduledService, JobStage.InProgress)] = ("complete-work", JobStage.AwaitingConfirmation),
        [(JobWorkflow.PickupAndDelivery, JobStage.Accepted)] = ("confirm-pickup", JobStage.PickedUp),
        [(JobWorkflow.PickupAndDelivery, JobStage.PickedUp)] = ("start-processing", JobStage.Processing),
        [(JobWorkflow.PickupAndDelivery, JobStage.Processing)] = ("mark-ready", JobStage.ReadyForDelivery),
        [(JobWorkflow.PickupAndDelivery, JobStage.ReadyForDelivery)] = ("start-delivery", JobStage.OutForDelivery),
        [(JobWorkflow.PickupAndDelivery, JobStage.OutForDelivery)] = ("confirm-delivery", JobStage.AwaitingConfirmation),
    };

    /// <summary>What the provider can do next, or null when the job is waiting on someone else or finished.</summary>
    public static (string Command, JobStage Next)? NextStep(Job job) =>
        Steps.TryGetValue((job.Workflow, job.Stage), out var step) ? step : null;

    public static JobWorkflow WorkflowFor(string categorySlug) =>
        categorySlug == "laundry" ? JobWorkflow.PickupAndDelivery : JobWorkflow.ScheduledService;

    /// <summary>
    /// Turns an offer into a job. Several providers hold offers for the same request, so the
    /// request is claimed with a single conditional update: whoever flips it from Searching wins,
    /// and everyone else gets <c>jobs.offer_already_accepted</c>.
    /// </summary>
    public async Task<Job> AcceptAsync(ProviderAccount account, Guid offerId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var offer = await FindOfferAsync(account.Id, offerId, ct);
        if (offer.Status == OfferStatus.Accepted)
            return await db.Jobs.SingleAsync(j => j.JobOfferId == offer.Id, ct);
        if (account.VerificationStatus != ProviderVerificationStatus.Approved) throw NotJobReady();
        if (offer.Status == OfferStatus.Withdrawn) throw AlreadyAccepted();
        if (offer.Status != OfferStatus.Pending || offer.ExpiresAt <= now) throw OfferGone();

        var request = await db.ServiceRequests.AsNoTracking().SingleAsync(r => r.Id == offer.ServiceRequestId, ct);
        var categorySlug = await db.ServiceCategories.Where(c => c.Id == request.ServiceCategoryId).Select(c => c.Slug).SingleAsync(ct);
        var feePercent = options.Value.FeePercent;
        var fee = Math.Round(offer.Price * feePercent / 100, 2, MidpointRounding.AwayFromZero);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.ServiceRequests
            .Where(r => r.Id == request.Id && r.Status == ServiceRequestStatus.Searching)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ServiceRequestStatus.Assigned)
                .SetProperty(r => r.NextDispatchAt, (DateTimeOffset?)null)
                .SetProperty(r => r.UpdatedAt, now), ct);
        if (claimed == 0) throw request.Status == ServiceRequestStatus.Cancelled ? OfferGone() : AlreadyAccepted();

        var job = new Job
        {
            ServiceRequestId = request.Id,
            JobOfferId = offer.Id,
            ProviderAccountId = account.Id,
            CustomerConsumerId = request.CustomerConsumerId,
            Workflow = WorkflowFor(categorySlug),
            Price = offer.Price,
            FeePercent = feePercent,
            Fee = fee,
            Net = offer.Price - fee,
            DistanceKm = offer.DistanceKm,
            ScheduledAt = request.ScheduledAt,
            AcceptedAt = now,
        };
        db.Jobs.Add(job);
        db.JobEvents.Add(new JobEvent { JobId = job.Id, Stage = JobStage.Accepted, ActorConsumerId = account.OwnerConsumerId });
        offer.Status = OfferStatus.Accepted;
        offer.RespondedAt = now;
        await db.JobOffers
            .Where(o => o.ServiceRequestId == request.Id && o.Id != offer.Id && o.Status == OfferStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OfferStatus.Withdrawn)
                .SetProperty(o => o.RespondedAt, now)
                .SetProperty(o => o.UpdatedAt, now), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(OneLiveJobPerRequestIndex))
        {
            throw AlreadyAccepted();
        }
        await tx.CommitAsync(ct);
        return job;
    }

    /// <summary>Turns an offer down. When that was the last open offer, the next round goes out straight away.</summary>
    public async Task DeclineAsync(ProviderAccount account, Guid offerId, string? reason, CancellationToken ct)
    {
        var offer = await FindOfferAsync(account.Id, offerId, ct);
        if (offer.Status == OfferStatus.Declined) return;
        if (offer.Status == OfferStatus.Accepted)
            throw Errors.Conflict("jobs.offer_already_accepted", "You already accepted this request.");
        if (offer.Status != OfferStatus.Pending) return;

        offer.Status = OfferStatus.Declined;
        offer.DeclineReason = reason;
        offer.RespondedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await dispatcher.DispatchAsync(offer.ServiceRequestId, ct);
    }

    /// <summary>Moves a job one step. <paramref name="command"/> must be the step the job is actually at.</summary>
    public async Task<Job> AdvanceAsync(ProviderAccount account, Guid jobId, string command, CancellationToken ct)
    {
        var job = await FindJobAsync(account.Id, jobId, ct);
        if (NextStep(job) is not { } step || step.Command != command)
        {
            // Repeating the step just taken (a retry after a lost response) is not an error.
            var previous = Steps.FirstOrDefault(s => s.Key.Item1 == job.Workflow && s.Value.Next == job.Stage);
            if (previous.Value.Command == command) return job;
            throw new ApiException(409, "jobs.invalid_transition", "This job can't take that step right now.",
                new { stage = Stage(job.Stage), nextCommand = NextStep(job)?.Command });
        }

        var now = time.GetUtcNow();
        var fromAccepted = job.Stage == JobStage.Accepted;
        job.Stage = step.Next;
        db.JobEvents.Add(new JobEvent { JobId = job.Id, Stage = step.Next, ActorConsumerId = account.OwnerConsumerId });

        if (fromAccepted)
        {
            await db.ServiceRequests.Where(r => r.Id == job.ServiceRequestId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, ServiceRequestStatus.InProgress)
                    .SetProperty(r => r.UpdatedAt, now), ct);
        }
        if (step.Next == JobStage.AwaitingConfirmation)
        {
            job.WorkCompletedAt = now;
            job.AutoConfirmAt = now.AddHours(options.Value.AutoConfirmHours);
            db.ProviderEarnings.Add(new ProviderEarning
            {
                ProviderAccountId = job.ProviderAccountId, JobId = job.Id,
                Gross = job.Price, Fee = job.Fee, Net = job.Net,
            });
        }
        await db.SaveChangesAsync(ct);
        return job;
    }

    /// <summary>
    /// A provider backing out before they set off. The request goes back to looking, and since a
    /// provider is offered a request once, it won't come back to them.
    /// </summary>
    public async Task<Job> CancelByProviderAsync(ProviderAccount account, Guid jobId, string reason, CancellationToken ct)
    {
        var job = await FindJobAsync(account.Id, jobId, ct);
        if (job.Stage == JobStage.Cancelled) return job;
        if (job.Stage != JobStage.Accepted)
            throw new ApiException(409, "jobs.invalid_transition",
                "This job has started, so it can't be cancelled here. Contact GRID support.", new { stage = Stage(job.Stage) });

        var now = time.GetUtcNow();
        job.Stage = JobStage.Cancelled;
        job.CancelledAt = now;
        job.CancelReason = reason;
        db.JobEvents.Add(new JobEvent { JobId = job.Id, Stage = JobStage.Cancelled, ActorConsumerId = account.OwnerConsumerId, Note = reason });
        var request = await db.ServiceRequests.SingleAsync(r => r.Id == job.ServiceRequestId, ct);
        request.Status = ServiceRequestStatus.Searching;
        request.NextDispatchAt = now;
        await db.SaveChangesAsync(ct);
        await dispatcher.DispatchAsync(request.Id, ct);
        return job;
    }

    /// <summary>
    /// Closes a finished job: the customer, Ops, or the clock once the customer has had long
    /// enough. The earning becomes available to pay out.
    /// </summary>
    public async Task<Job> ConfirmAsync(Job job, Guid? actorConsumerId, CancellationToken ct)
    {
        if (job.Stage == JobStage.Completed) return job;
        if (job.Stage != JobStage.AwaitingConfirmation)
            throw new ApiException(409, "jobs.invalid_transition", "This job isn't finished yet.", new { stage = Stage(job.Stage) });

        var now = time.GetUtcNow();
        job.Stage = JobStage.Completed;
        job.ConfirmedAt = now;
        job.AutoConfirmAt = null;
        db.JobEvents.Add(new JobEvent
        {
            JobId = job.Id, Stage = JobStage.Completed, ActorConsumerId = actorConsumerId,
            Note = actorConsumerId is null ? "Confirmed automatically." : null,
        });
        var request = await db.ServiceRequests.SingleAsync(r => r.Id == job.ServiceRequestId, ct);
        request.Status = ServiceRequestStatus.Completed;
        var earning = await db.ProviderEarnings.SingleAsync(e => e.JobId == job.Id, ct);
        earning.Status = EarningStatus.Available;
        earning.AvailableAt = now;
        await db.SaveChangesAsync(ct);
        return job;
    }

    /// <summary>
    /// A customer (or Ops) calling a booking off, before the provider sets off. Anything paid is
    /// refunded in full.
    /// </summary>
    public async Task CancelRequestAsync(ServiceRequest request, Guid? actorConsumerId, string? reason, CancellationToken ct)
    {
        if (request.Status == ServiceRequestStatus.Cancelled) return;
        if (request.Status is not (ServiceRequestStatus.AwaitingPayment or ServiceRequestStatus.PaymentFailed
            or ServiceRequestStatus.Searching or ServiceRequestStatus.Assigned or ServiceRequestStatus.Unfilled))
            throw new ApiException(409, "services.request_not_cancellable",
                "This booking is already under way. Contact GRID support to change it.");

        var now = time.GetUtcNow();
        request.Status = ServiceRequestStatus.Cancelled;
        request.CancelledAt = now;
        request.CancelReason = reason;
        request.NextDispatchAt = null;

        var job = await db.Jobs.SingleOrDefaultAsync(j => j.ServiceRequestId == request.Id && j.Stage != JobStage.Cancelled, ct);
        if (job is not null)
        {
            job.Stage = JobStage.Cancelled;
            job.CancelledAt = now;
            job.CancelReason = reason ?? "Cancelled by the customer.";
            db.JobEvents.Add(new JobEvent { JobId = job.Id, Stage = JobStage.Cancelled, ActorConsumerId = actorConsumerId, Note = reason });
        }
        await db.JobOffers
            .Where(o => o.ServiceRequestId == request.Id && o.Status == OfferStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OfferStatus.Withdrawn)
                .SetProperty(o => o.RespondedAt, now)
                .SetProperty(o => o.UpdatedAt, now), ct);
        await payments.RequestRefundAsync(request.Id, "Cancelled", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Stage codes are camelCase on the wire, e.g. <c>onTheWay</c>.</summary>
    public static string Stage(JobStage stage)
    {
        var name = stage.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private async Task<JobOffer> FindOfferAsync(Guid accountId, Guid offerId, CancellationToken ct) =>
        await db.JobOffers.SingleOrDefaultAsync(o => o.Id == offerId && o.ProviderAccountId == accountId, ct)
        ?? throw Errors.NotFound("jobs.offer_not_found", "That request was not found.");

    private async Task<Job> FindJobAsync(Guid accountId, Guid jobId, CancellationToken ct) =>
        await db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId && j.ProviderAccountId == accountId, ct)
        ?? throw Errors.NotFound("jobs.not_found", "That job was not found.");

    private static ApiException AlreadyAccepted() =>
        Errors.Conflict("jobs.offer_already_accepted", "Another professional took this request first.");

    private static ApiException OfferGone() =>
        Errors.Conflict("jobs.offer_expired", "This request is no longer available.");

    private static ApiException NotJobReady() =>
        new(409, "providers.not_job_ready", "Your account can't take jobs right now.");
}
