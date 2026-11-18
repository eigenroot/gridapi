using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Marketplace;

/// <summary>
/// A request as a provider sees it before accepting: enough to decide, and nothing that
/// identifies the customer. The name, phone and address arrive with the job.
/// </summary>
public sealed record JobOfferResponse(
    Guid Id,
    string RequestReference,
    string ServiceCategory,
    string Title,
    string? Scope,
    List<string> Items,
    string? Notes,
    string GeneralArea,
    decimal? DistanceKm,
    DateTimeOffset ScheduledAt,
    RequestUrgency Urgency,
    decimal Price,
    decimal FeePercent,
    decimal Fee,
    decimal Earnings,
    OfferStatus Status,
    DateTimeOffset ExpiresAt,
    Guid? JobId);

/// <summary>
/// <c>stage</c> is camelCase. <c>nextCommand</c> is the one step the provider can take now
/// (<c>POST /jobs/{id}/{nextCommand}</c>), or null when the job is waiting on the customer or closed.
/// The customer's phone is only present while the job is live.
/// </summary>
public sealed record JobResponse(
    Guid Id,
    string JobCode,
    string ServiceCategory,
    JobWorkflow Workflow,
    string Title,
    string? Scope,
    List<string> Items,
    string? Notes,
    string CustomerName,
    string? CustomerPhone,
    string Area,
    string City,
    string Address,
    string? GhanaPostGps,
    double? Latitude,
    double? Longitude,
    decimal? DistanceKm,
    DateTimeOffset ScheduledAt,
    decimal Price,
    decimal FeePercent,
    decimal Fee,
    decimal Earnings,
    string Stage,
    string? NextCommand,
    string? NextStage,
    string LifecycleStatus,
    DateTimeOffset AcceptedAt,
    DateTimeOffset? WorkCompletedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason);

public sealed record DeclineOfferRequest(string? Reason);

public sealed record CancelJobRequest(string? Reason);

public sealed record EarningsSummaryResponse(
    decimal AvailableAmount,
    decimal PendingAmount,
    decimal PaidAmount,
    decimal Last7Days,
    decimal Last30Days,
    int JobsCount,
    decimal FeePercent);

public sealed record EarningResponse(
    Guid Id,
    Guid JobId,
    string JobCode,
    string ServiceCategory,
    string Title,
    string CustomerName,
    decimal Gross,
    decimal Fee,
    decimal Net,
    EarningStatus Status,
    DateTimeOffset EarnedAt);

public sealed record PayoutResponse(
    Guid Id,
    string Reference,
    decimal Amount,
    PayoutStatus Status,
    string Destination,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt);

/// <summary>The provider's side of the marketplace: offers, jobs and what they have earned.</summary>
public static class ProviderWorkEndpoints
{
    public static void MapProviderWorkEndpoints(this IEndpointRouteBuilder api)
    {
        var offers = api.MapGroup("/job-offers").WithTags("Provider work").RequireAuthorization();
        offers.MapGet("", ListOffers);
        offers.MapGet("/{id:guid}", GetOffer);
        offers.MapPost("/{id:guid}/accept", Accept);
        offers.MapPost("/{id:guid}/decline", Decline);

        var jobs = api.MapGroup("/jobs").WithTags("Provider work").RequireAuthorization();
        jobs.MapGet("", ListJobs);
        jobs.MapGet("/{id:guid}", GetJob);
        jobs.MapPost("/{id:guid}/cancel", Cancel);
        jobs.MapPost("/{id:guid}/{command}", Advance);

        var earnings = api.MapGroup("/provider-earnings").WithTags("Provider work").RequireAuthorization();
        earnings.MapGet("", ListEarnings);
        earnings.MapGet("/summary", Summary);

        api.MapGet("/provider-payouts", ListPayouts).WithTags("Provider work").RequireAuthorization();
    }

    // ── Offers ─────────────────────────────────────────────────────────────

    /// <summary><c>status=Pending</c> (the default) lists open offers that haven't run out, soonest to expire first.</summary>
    private static async Task<EnvelopeResult<List<JobOfferResponse>>> ListOffers(
        OfferStatus? status, CurrentProvider provider, GridDbContext db, TimeProvider time,
        IOptions<MarketplaceOptions> options, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var wanted = status ?? OfferStatus.Pending;
        var now = time.GetUtcNow();
        var offers = db.JobOffers.Where(o => o.ProviderAccountId == accountId && o.Status == wanted);
        if (wanted == OfferStatus.Pending) offers = offers.Where(o => o.ExpiresAt > now);

        var rows = (await OfferRows(db, offers.OrderBy(o => o.ExpiresAt).Take(50)).ToListAsync(ct))
            .OrderBy(x => x.Offer.ExpiresAt);
        return Envelope.Ok(rows.Select(r => Map(r, options.Value.FeePercent)).ToList());
    }

    private static async Task<EnvelopeResult<JobOfferResponse>> GetOffer(
        Guid id, CurrentProvider provider, GridDbContext db, IOptions<MarketplaceOptions> options, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var row = await OfferRows(db, db.JobOffers.Where(o => o.Id == id && o.ProviderAccountId == accountId)).SingleOrDefaultAsync(ct)
                  ?? throw Errors.NotFound("jobs.offer_not_found", "That request was not found.");
        return Envelope.Ok(Map(row, options.Value.FeePercent));
    }

    private static async Task<EnvelopeResult<JobResponse>> Accept(
        Guid id, CurrentProvider provider, JobService jobs, GridDbContext db, CancellationToken ct)
    {
        var account = (await provider.LoadAsync(ct)).Account;
        var job = await jobs.AcceptAsync(account, id, ct);
        return Envelope.Ok(await JobAsync(db, job.Id, ct));
    }

    private static async Task<EnvelopeResult<object>> Decline(
        Guid id, HttpRequest http, CurrentProvider provider, JobService jobs, CancellationToken ct)
    {
        var body = await OptionalBody.ReadAsync<DeclineOfferRequest>(http, ct);
        var v = new Validator();
        var reason = v.Optional("reason", body?.Reason, 200, "Reason");
        v.ThrowIfInvalid();
        var account = (await provider.LoadAsync(ct)).Account;
        await jobs.DeclineAsync(account, id, reason, ct);
        return Envelope.Empty();
    }

    // ── Jobs ───────────────────────────────────────────────────────────────

    /// <summary><c>status</c> is <c>active</c> (the default: live jobs, soonest first) or <c>completed</c> (finished and cancelled, newest first).</summary>
    private static async Task<EnvelopeResult<List<JobResponse>>> ListJobs(
        string? status, CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var mine = db.Jobs.Where(j => j.ProviderAccountId == accountId);
        var rows = (status ?? "active").ToLowerInvariant() switch
        {
            "active" => (await JobRows(db, mine
                    .Where(j => j.Stage != JobStage.AwaitingConfirmation && j.Stage != JobStage.Completed && j.Stage != JobStage.Cancelled)
                    .OrderBy(j => j.ScheduledAt).Take(100)).ToListAsync(ct))
                .OrderBy(x => x.Job.ScheduledAt).ToList(),
            "completed" => (await JobRows(db, mine
                    .Where(j => j.Stage == JobStage.AwaitingConfirmation || j.Stage == JobStage.Completed || j.Stage == JobStage.Cancelled)
                    .OrderByDescending(j => j.ScheduledAt).Take(100)).ToListAsync(ct))
                .OrderByDescending(x => x.Job.ScheduledAt).ToList(),
            _ => throw Errors.Validation([new FieldError("status", "Use active or completed.")]),
        };
        return Envelope.Ok(rows.Select(x => Map(x.Job, x.Request, x.Slug, x.Phone)).ToList());
    }

    private static async Task<EnvelopeResult<JobResponse>> GetJob(
        Guid id, CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        return Envelope.Ok(await JobAsync(db, id, ct, accountId));
    }

    /// <summary>
    /// Takes the job's next step. The command must be the job's current <c>nextCommand</c>:
    /// <c>start-journey</c>, <c>arrive</c>, <c>start-work</c>, <c>complete-work</c> for cleaning;
    /// <c>confirm-pickup</c>, <c>start-processing</c>, <c>mark-ready</c>, <c>start-delivery</c>,
    /// <c>confirm-delivery</c> for laundry.
    /// </summary>
    private static async Task<EnvelopeResult<JobResponse>> Advance(
        Guid id, string command, CurrentProvider provider, JobService jobs, GridDbContext db, CancellationToken ct)
    {
        var account = (await provider.LoadAsync(ct)).Account;
        var job = await jobs.AdvanceAsync(account, id, command, ct);
        return Envelope.Ok(await JobAsync(db, job.Id, ct));
    }

    private static async Task<EnvelopeResult<JobResponse>> Cancel(
        Guid id, HttpRequest http, CurrentProvider provider, JobService jobs, GridDbContext db, CancellationToken ct)
    {
        var body = await OptionalBody.ReadAsync<CancelJobRequest>(http, ct);
        var v = new Validator();
        var reason = v.Required("reason", body?.Reason, 300, "Reason");
        v.ThrowIfInvalid();
        var account = (await provider.LoadAsync(ct)).Account;
        var job = await jobs.CancelByProviderAsync(account, id, reason, ct);
        return Envelope.Ok(await JobAsync(db, job.Id, ct));
    }

    // ── Earnings ───────────────────────────────────────────────────────────

    private static async Task<EnvelopeResult<EarningsSummaryResponse>> Summary(
        CurrentProvider provider, GridDbContext db, TimeProvider time,
        IOptions<MarketplaceOptions> options, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var now = time.GetUtcNow();
        var rows = await db.ProviderEarnings
            .Where(e => e.ProviderAccountId == accountId && e.Status != EarningStatus.Reversed)
            .Select(e => new { e.Status, e.Net, e.CreatedAt })
            .ToListAsync(ct);

        decimal Total(EarningStatus status) => rows.Where(r => r.Status == status).Sum(r => r.Net);
        decimal Since(int days) => rows.Where(r => r.CreatedAt > now.AddDays(-days)).Sum(r => r.Net);
        return Envelope.Ok(new EarningsSummaryResponse(
            Total(EarningStatus.Available), Total(EarningStatus.Pending), Total(EarningStatus.Paid),
            Since(7), Since(30), rows.Count, options.Value.FeePercent));
    }

    private static async Task<EnvelopeResult<List<EarningResponse>>> ListEarnings(
        CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var rows = await (
            from earning in db.ProviderEarnings
            join job in db.Jobs on earning.JobId equals job.Id
            join request in db.ServiceRequests on job.ServiceRequestId equals request.Id
            join category in db.ServiceCategories on request.ServiceCategoryId equals category.Id
            where earning.ProviderAccountId == accountId
            orderby earning.CreatedAt descending
            select new { earning, job.Number, request.Title, request.CustomerName, category.Slug }).Take(100).ToListAsync(ct);
        return Envelope.Ok(rows.Select(r => new EarningResponse(
            r.earning.Id, r.earning.JobId, $"GJ-{r.Number}", r.Slug, r.Title, r.CustomerName,
            r.earning.Gross, r.earning.Fee, r.earning.Net, r.earning.Status, r.earning.CreatedAt)).ToList());
    }

    private static async Task<EnvelopeResult<List<PayoutResponse>>> ListPayouts(
        CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var rows = await db.ProviderPayouts.Where(p => p.ProviderAccountId == accountId)
            .OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync(ct);
        return Envelope.Ok(rows.Select(Map).ToList());
    }

    // ── Shapes ─────────────────────────────────────────────────────────────

    internal static PayoutResponse Map(ProviderPayout p) =>
        new(p.Id, p.Reference, p.Amount, p.Status, p.Destination, p.CreatedAt, p.PaidAt);

    private sealed record OfferRow(JobOffer Offer, ServiceRequest Request, string Slug, Guid? JobId);

    private static IQueryable<OfferRow> OfferRows(GridDbContext db, IQueryable<JobOffer> offers) =>
        from offer in offers
        join request in db.ServiceRequests on offer.ServiceRequestId equals request.Id
        join category in db.ServiceCategories on request.ServiceCategoryId equals category.Id
        select new OfferRow(offer, request, category.Slug,
            db.Jobs.Where(j => j.JobOfferId == offer.Id).Select(j => (Guid?)j.Id).FirstOrDefault());

    private static JobOfferResponse Map(OfferRow row, decimal feePercent)
    {
        var (offer, request, slug, jobId) = row;
        var fee = Math.Round(offer.Price * feePercent / 100, 2, MidpointRounding.AwayFromZero);
        return new JobOfferResponse(
            offer.Id, request.Reference, slug, request.Title, request.Scope, request.Items, request.Notes,
            request.Area, offer.DistanceKm, request.ScheduledAt, request.Urgency, offer.Price,
            feePercent, fee, offer.Price - fee, offer.Status, offer.ExpiresAt, jobId);
    }

    private sealed record JobRow(Job Job, ServiceRequest Request, string Slug, string Phone);

    private static IQueryable<JobRow> JobRows(GridDbContext db, IQueryable<Job> jobs) =>
        from job in jobs
        join request in db.ServiceRequests on job.ServiceRequestId equals request.Id
        join category in db.ServiceCategories on request.ServiceCategoryId equals category.Id
        join customer in db.Consumers on job.CustomerConsumerId equals customer.Id
        select new JobRow(job, request, category.Slug, customer.PhoneNumber);

    internal static async Task<JobResponse> JobAsync(GridDbContext db, Guid jobId, CancellationToken ct, Guid? accountId = null)
    {
        var row = await JobRows(db, db.Jobs.Where(j => j.Id == jobId && (accountId == null || j.ProviderAccountId == accountId)))
                      .SingleOrDefaultAsync(ct)
                  ?? throw Errors.NotFound("jobs.not_found", "That job was not found.");
        return Map(row.Job, row.Request, row.Slug, row.Phone);
    }

    internal static JobResponse Map(Job job, ServiceRequest request, string slug, string customerPhone)
    {
        var next = JobService.NextStep(job);
        var lifecycle = job.Stage switch
        {
            JobStage.AwaitingConfirmation => "AwaitingConfirmation",
            JobStage.Completed => "Completed",
            JobStage.Cancelled => "Cancelled",
            _ => "Active",
        };
        return new JobResponse(
            job.Id, job.Code, slug, job.Workflow, request.Title, request.Scope, request.Items, request.Notes,
            request.CustomerName, job.IsActive ? customerPhone : null,
            request.Area, request.City, request.Address, request.GhanaPostGps, request.Latitude, request.Longitude,
            job.DistanceKm, job.ScheduledAt, job.Price, job.FeePercent, job.Fee, job.Net,
            JobService.Stage(job.Stage), next?.Command, next is { } step ? JobService.Stage(step.Next) : null,
            lifecycle, job.AcceptedAt, job.WorkCompletedAt, job.ConfirmedAt, job.CancelledAt, job.CancelReason);
    }
}
