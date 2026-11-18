using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Consumers;
using Grid.Api.Features.Marketplace;
using Grid.Api.Features.Media;
using Grid.Api.Features.Providers;
using Grid.Api.Features.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Ops;

/// <summary>Lets the phone numbers in <c>Ops:Operators</c> through; everyone else gets <c>ops.forbidden</c>.</summary>
public sealed class OpsAccess(CurrentConsumer current, IOptions<OpsOptions> options)
{
    public async Task<Consumer> RequireAsync(CancellationToken ct)
    {
        var consumer = await current.LoadAsync(ct);
        var operators = options.Value.Operators.Select(p => PhoneNumbers.TryNormalize(p, out var n) ? n : null);
        if (!operators.Contains(consumer.PhoneNumber)) throw new ApiException(403, "ops.forbidden", "GRID Ops access is required.");
        return consumer;
    }
}

public sealed record OpsApplicationRow(
    Guid ApplicationId, Guid ProviderAccountId, string DisplayName, ProviderAccountType AccountType, string PhoneNumber,
    ApplicationStatus Status, int Version, DateTimeOffset? SubmittedAt);

public sealed record OpsApplicationDetail(
    ApplicationResponse Application,
    ProviderAccountResponse Account,
    string OwnerName,
    string PhoneNumber,
    string? KycName,
    List<OfferingResponse> Offerings,
    List<string> ServiceAreas,
    List<AvailabilityRuleResponse> Availability,
    List<VerificationDocumentResponse> Documents,
    List<PortfolioItemResponse> Portfolio);

public sealed record ReviewNoteRequest(string? Note);

public sealed record OpsRequestRow(
    Guid Id, string Reference, string Title, string Area, DateTimeOffset ScheduledAt, ServiceRequestStatus Status,
    int DispatchRound, int OffersSent, string CustomerName, string CustomerPhone);

public sealed record RecordPayoutRequest(Guid? ProviderAccountId, string? ExternalReference);

/// <summary>
/// What GRID staff need until the Ops console exists: review applications, look after bookings
/// nobody took, close jobs, record payouts and answer tickets. Driven with curl for now.
/// </summary>
public static class OpsEndpoints
{
    public static void MapOpsEndpoints(this IEndpointRouteBuilder api)
    {
        var ops = api.MapGroup("/ops").WithTags("Ops").RequireAuthorization();

        ops.MapGet("/provider-applications", ListApplications);
        ops.MapGet("/provider-applications/{id:guid}", GetApplication);
        ops.MapPost("/provider-applications/{id:guid}/start-review", StartReview);
        ops.MapPost("/provider-applications/{id:guid}/approve", Approve);
        ops.MapPost("/provider-applications/{id:guid}/request-information", RequestInformation);
        ops.MapPost("/provider-applications/{id:guid}/reject", Reject);
        ops.MapPost("/verification-documents/{id:guid}/approve", ApproveDocument);
        ops.MapPost("/verification-documents/{id:guid}/reject", RejectDocument);
        ops.MapGet("/media/{storageKey}", DownloadMedia);

        ops.MapGet("/service-requests", ListRequests);
        ops.MapPost("/service-requests/{id:guid}/redispatch", Redispatch);
        ops.MapPost("/service-requests/{id:guid}/cancel", CancelRequest);
        ops.MapPost("/service-requests/{id:guid}/refund", RefundRequest);
        ops.MapPost("/jobs/{id:guid}/confirm-completion", ConfirmJob);

        ops.MapPost("/provider-payouts", RecordPayout);

        ops.MapGet("/support-tickets", ListTickets);
        ops.MapPost("/support-tickets/{id:guid}/resolve", ResolveTicket);
    }

    // ── Applications ───────────────────────────────────────────────────────

    /// <summary>Defaults to the review queue: <c>Submitted</c> and <c>UnderReview</c>, oldest first.</summary>
    private static async Task<EnvelopeResult<List<OpsApplicationRow>>> ListApplications(
        ApplicationStatus? status, OpsAccess ops, GridDbContext db, CancellationToken ct)
    {
        await ops.RequireAsync(ct);
        var rows = await (
            from application in db.ProviderApplications
            join account in db.ProviderAccounts on application.ProviderAccountId equals account.Id
            join owner in db.Consumers on account.OwnerConsumerId equals owner.Id
            where status == null
                ? application.Status == ApplicationStatus.Submitted || application.Status == ApplicationStatus.UnderReview
                : application.Status == status
            orderby application.SubmittedAt
            select new OpsApplicationRow(application.Id, account.Id, account.DisplayName, account.AccountType,
                owner.PhoneNumber, application.Status, application.Version, application.SubmittedAt)).Take(200).ToListAsync(ct);
        return Envelope.Ok(rows);
    }

    private static async Task<EnvelopeResult<OpsApplicationDetail>> GetApplication(
        Guid id, OpsAccess ops, GridDbContext db, CancellationToken ct)
    {
        await ops.RequireAsync(ct);
        var (application, account) = await FindAsync(db, id, ct);
        var owner = await db.Consumers.SingleAsync(c => c.Id == account.OwnerConsumerId, ct);
        var accountId = account.Id;
        return Envelope.Ok(new OpsApplicationDetail(
            ApplicationResponse.From(application),
            ProviderAccountResponse.From(account),
            owner.FullName, owner.PhoneNumber, owner.KycName,
            (await db.ProviderOfferings.Where(o => o.ProviderAccountId == accountId).ToListAsync(ct)).Select(OfferingResponse.From).ToList(),
            await (from row in db.ProviderServiceAreas
                join area in db.ServiceAreas on row.ServiceAreaId equals area.Id
                where row.ProviderAccountId == accountId
                orderby area.SortOrder
                select area.Name).ToListAsync(ct),
            (await db.ProviderAvailabilityRules.Where(r => r.ProviderAccountId == accountId).ToListAsync(ct))
                .Select(AvailabilityRuleResponse.From).ToList(),
            (await db.VerificationDocuments.Where(d => d.ProviderAccountId == accountId && d.SupersededAt == null)
                .OrderBy(d => d.CreatedAt).ToListAsync(ct)).Select(VerificationDocumentResponse.From).ToList(),
            (await db.PortfolioItems.Where(p => p.ProviderAccountId == accountId && p.ArchivedAt == null)
                .OrderBy(p => p.CreatedAt).ToListAsync(ct)).Select(PortfolioItemResponse.From).ToList()));
    }

    private static Task<EnvelopeResult<ApplicationResponse>> StartReview(
        Guid id, OpsAccess ops, GridDbContext db, TimeProvider time, CancellationToken ct) =>
        DecideAsync(id, ops, db, time, ApplicationStatus.UnderReview, null, [ApplicationStatus.Submitted], ct);

    private static Task<EnvelopeResult<ApplicationResponse>> Approve(
        Guid id, HttpRequest http, OpsAccess ops, GridDbContext db, TimeProvider time, CancellationToken ct) =>
        DecideAsync(id, ops, db, time, ApplicationStatus.Approved, http,
            [ApplicationStatus.Submitted, ApplicationStatus.UnderReview], ct);

    /// <summary>Sends it back to the provider with a note saying what to fix; their details unlock until they resubmit.</summary>
    private static Task<EnvelopeResult<ApplicationResponse>> RequestInformation(
        Guid id, HttpRequest http, OpsAccess ops, GridDbContext db, TimeProvider time, CancellationToken ct) =>
        DecideAsync(id, ops, db, time, ApplicationStatus.MoreInformationRequired, http,
            [ApplicationStatus.Submitted, ApplicationStatus.UnderReview], ct, noteRequired: true);

    private static Task<EnvelopeResult<ApplicationResponse>> Reject(
        Guid id, HttpRequest http, OpsAccess ops, GridDbContext db, TimeProvider time, CancellationToken ct) =>
        DecideAsync(id, ops, db, time, ApplicationStatus.Rejected, http,
            [ApplicationStatus.Submitted, ApplicationStatus.UnderReview], ct, noteRequired: true);

    private static async Task<EnvelopeResult<ApplicationResponse>> DecideAsync(
        Guid id, OpsAccess ops, GridDbContext db, TimeProvider time, ApplicationStatus decision, HttpRequest? http,
        ApplicationStatus[] from, CancellationToken ct, bool noteRequired = false)
    {
        await ops.RequireAsync(ct);
        var note = http is null ? null : (await OptionalBody.ReadAsync<ReviewNoteRequest>(http, ct))?.Note;
        var v = new Validator();
        var text = noteRequired ? v.Required("note", note, 2000, "Note") : v.Optional("note", note, 2000, "Note");
        v.ThrowIfInvalid();

        var (application, account) = await FindAsync(db, id, ct);
        if (application.Status == decision) return Envelope.Ok(ApplicationResponse.From(application));
        if (!from.Contains(application.Status))
            throw Errors.Conflict("ops.application_not_reviewable", $"This application is {application.Status}, so that decision doesn't apply.");

        application.Status = decision;
        application.ReviewedAt = time.GetUtcNow();
        if (decision == ApplicationStatus.Rejected) application.RejectionReason = text;
        else if (decision != ApplicationStatus.UnderReview) application.ReviewNote = text;
        account.VerificationStatus = Enum.Parse<ProviderVerificationStatus>(decision.ToString());
        // Someone no longer approved can't be left taking jobs.
        if (decision != ApplicationStatus.Approved) account.OperatingStatus = OperatingStatus.Offline;
        await db.SaveChangesAsync(ct);
        return Envelope.Ok(ApplicationResponse.From(application));
    }

    private static Task<EnvelopeResult<VerificationDocumentResponse>> ApproveDocument(
        Guid id, OpsAccess ops, GridDbContext db, TimeProvider time, CancellationToken ct) =>
        ReviewDocumentAsync(id, ops, db, time, ReviewStatus.Approved, null, ct);

    private static Task<EnvelopeResult<VerificationDocumentResponse>> RejectDocument(
        Guid id, HttpRequest http, OpsAccess ops, GridDbContext db, TimeProvider time, CancellationToken ct) =>
        ReviewDocumentAsync(id, ops, db, time, ReviewStatus.Rejected, http, ct);

    private static async Task<EnvelopeResult<VerificationDocumentResponse>> ReviewDocumentAsync(
        Guid id, OpsAccess ops, GridDbContext db, TimeProvider time, ReviewStatus decision, HttpRequest? http, CancellationToken ct)
    {
        var reviewer = await ops.RequireAsync(ct);
        var note = http is null ? null : (await OptionalBody.ReadAsync<ReviewNoteRequest>(http, ct))?.Note;
        var v = new Validator();
        var text = decision == ReviewStatus.Rejected ? v.Required("note", note, 2000, "Note") : null;
        v.ThrowIfInvalid();

        var document = await db.VerificationDocuments.SingleOrDefaultAsync(d => d.Id == id, ct)
                       ?? throw Errors.NotFound("providers.document_not_found", "That document was not found.");
        document.ReviewStatus = decision;
        document.ReviewNote = text;
        document.ReviewedAt = time.GetUtcNow();
        document.ReviewedBy = reviewer.Id;
        await db.SaveChangesAsync(ct);
        return Envelope.Ok(VerificationDocumentResponse.From(document));
    }

    /// <summary>Any confirmed upload, for reviewing identity documents and portfolio photos.</summary>
    private static async Task<IResult> DownloadMedia(
        string storageKey, OpsAccess ops, GridDbContext db, IMediaStore store, CancellationToken ct)
    {
        await ops.RequireAsync(ct);
        var media = MediaKeys.IsValid(storageKey)
            ? await db.MediaObjects.SingleOrDefaultAsync(m => m.StorageKey == storageKey && m.Status == MediaStatus.Confirmed, ct)
            : null;
        var stream = media is null ? null : await store.OpenReadAsync(media.StorageKey, ct);
        if (stream is null) throw Errors.NotFound("media.not_found", "That file was not found.");
        return Results.Stream(stream, media!.ContentType);
    }

    // ── Bookings and jobs ──────────────────────────────────────────────────

    /// <summary>Defaults to <c>Unfilled</c>: bookings every round of offers ran out on.</summary>
    private static async Task<EnvelopeResult<List<OpsRequestRow>>> ListRequests(
        ServiceRequestStatus? status, OpsAccess ops, GridDbContext db, CancellationToken ct)
    {
        await ops.RequireAsync(ct);
        var wanted = status ?? ServiceRequestStatus.Unfilled;
        var rows = await (
            from request in db.ServiceRequests
            join customer in db.Consumers on request.CustomerConsumerId equals customer.Id
            where request.Status == wanted
            orderby request.ScheduledAt
            select new OpsRequestRow(request.Id, "GR-" + request.Number, request.Title, request.Area, request.ScheduledAt,
                request.Status, request.DispatchRound, db.JobOffers.Count(o => o.ServiceRequestId == request.Id),
                request.CustomerName, customer.PhoneNumber)).Take(200).ToListAsync(ct);
        return Envelope.Ok(rows);
    }

    /// <summary>
    /// Refunds what a booking collected, for example after a dispute. The booking keeps its status;
    /// the refund shows on its payment.
    /// </summary>
    private static async Task<EnvelopeResult<ServiceRequestResponse>> RefundRequest(
        Guid id, HttpRequest http, OpsAccess ops, GridDbContext db, ServicePayments.ServicePaymentService payments, CancellationToken ct)
    {
        await ops.RequireAsync(ct);
        var v = new Validator();
        var note = v.Required("note", (await OptionalBody.ReadAsync<ReviewNoteRequest>(http, ct))?.Note, 60, "Reason");
        v.ThrowIfInvalid();
        var request = await FindRequestAsync(db, id, ct);
        if (!await db.ServicePayments.AnyAsync(p => p.ServiceRequestId == id && p.State == BillPayments.TxnState.Succeeded && p.RefundState == null, ct))
            throw Errors.Conflict("payments.nothing_to_refund", "This booking has no payment left to refund.");
        await payments.RequestRefundAsync(request.Id, note, ct);
        await db.SaveChangesAsync(ct);
        return Envelope.Ok(await ServiceRequestEndpoints.ResponseAsync(db, request.Id, ct));
    }

    /// <summary>Starts the rounds again for a booking still looking, for example once more providers are online.</summary>
    private static async Task<EnvelopeResult<ServiceRequestResponse>> Redispatch(
        Guid id, OpsAccess ops, GridDbContext db, Dispatcher dispatcher, CancellationToken ct)
    {
        await ops.RequireAsync(ct);
        var request = await FindRequestAsync(db, id, ct);
        // An unfilled booking has been refunded, so only one still searching can start again.
        if (request.Status != ServiceRequestStatus.Searching)
            throw Errors.Conflict("services.request_not_dispatchable", "This booking isn't looking for a provider.");
        request.Status = ServiceRequestStatus.Searching;
        request.DispatchRound = 0;
        await db.SaveChangesAsync(ct);
        await dispatcher.DispatchAsync(request.Id, ct);
        return Envelope.Ok(await ServiceRequestEndpoints.ResponseAsync(db, request.Id, ct));
    }

    private static async Task<EnvelopeResult<ServiceRequestResponse>> CancelRequest(
        Guid id, HttpRequest http, OpsAccess ops, GridDbContext db, JobService jobs, CancellationToken ct)
    {
        var staff = await ops.RequireAsync(ct);
        var request = await FindRequestAsync(db, id, ct);
        var note = new Validator().Optional("note", (await OptionalBody.ReadAsync<ReviewNoteRequest>(http, ct))?.Note, 300, "Note");
        await jobs.CancelRequestAsync(request, staff.Id, note, ct);
        return Envelope.Ok(await ServiceRequestEndpoints.ResponseAsync(db, request.Id, ct));
    }

    /// <summary>Closes a finished job on the customer's behalf.</summary>
    private static async Task<EnvelopeResult<JobResponse>> ConfirmJob(
        Guid id, OpsAccess ops, GridDbContext db, JobService jobs, CancellationToken ct)
    {
        var staff = await ops.RequireAsync(ct);
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == id, ct)
                  ?? throw Errors.NotFound("jobs.not_found", "That job was not found.");
        await jobs.ConfirmAsync(job, staff.Id, ct);
        return Envelope.Ok(await ProviderWorkEndpoints.JobAsync(db, job.Id, ct));
    }

    // ── Payouts ────────────────────────────────────────────────────────────

    /// <summary>
    /// Records that GRID has paid a provider everything they had available, to their default
    /// payout method. The money itself is sent outside the API; this is the record of it.
    /// </summary>
    private static async Task<EnvelopeResult<PayoutResponse>> RecordPayout(
        RecordPayoutRequest body, OpsAccess ops, GridDbContext db, TimeProvider time, CancellationToken ct)
    {
        var staff = await ops.RequireAsync(ct);
        var v = new Validator();
        if (body.ProviderAccountId is null) v.Add("providerAccountId", "Choose a provider.");
        var reference = v.Optional("externalReference", body.ExternalReference, 100, "Reference");
        v.ThrowIfInvalid();

        var accountId = body.ProviderAccountId!.Value;
        var method = await db.PayoutMethods.SingleOrDefaultAsync(m => m.ProviderAccountId == accountId && m.IsDefault && m.ArchivedAt == null, ct)
                     ?? throw Errors.Conflict("payouts.no_method", "This provider has no payout method.");

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var earnings = await db.ProviderEarnings
            .Where(e => e.ProviderAccountId == accountId && e.Status == EarningStatus.Available).ToListAsync(ct);
        if (earnings.Count == 0) throw Errors.Conflict("payouts.nothing_available", "This provider has nothing available to pay out.");

        var now = time.GetUtcNow();
        var payout = new ProviderPayout
        {
            ProviderAccountId = accountId,
            Amount = earnings.Sum(e => e.Net),
            Status = PayoutStatus.Paid,
            Destination = $"{method.ProviderName} {method.MaskedAccountNumber}",
            ExternalReference = reference,
            PaidAt = now,
            RecordedByConsumerId = staff.Id,
        };
        db.ProviderPayouts.Add(payout);
        foreach (var earning in earnings)
        {
            earning.Status = EarningStatus.Paid;
            earning.PayoutId = payout.Id;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Envelope.Created(ProviderWorkEndpoints.Map(payout));
    }

    // ── Support ────────────────────────────────────────────────────────────

    private static async Task<EnvelopeResult<List<TicketResponse>>> ListTickets(
        TicketStatus? status, OpsAccess ops, GridDbContext db, CancellationToken ct)
    {
        await ops.RequireAsync(ct);
        var wanted = status ?? TicketStatus.Open;
        var rows = await db.SupportTickets.Where(t => t.Status == wanted).OrderBy(t => t.CreatedAt).Take(200).ToListAsync(ct);
        return Envelope.Ok(rows.Select(TicketResponse.From).ToList());
    }

    private static async Task<EnvelopeResult<TicketResponse>> ResolveTicket(
        Guid id, HttpRequest http, OpsAccess ops, GridDbContext db, TimeProvider time, CancellationToken ct)
    {
        await ops.RequireAsync(ct);
        var v = new Validator();
        var note = v.Required("note", (await OptionalBody.ReadAsync<ReviewNoteRequest>(http, ct))?.Note, 2000, "Note");
        v.ThrowIfInvalid();
        var ticket = await db.SupportTickets.SingleOrDefaultAsync(t => t.Id == id, ct)
                     ?? throw Errors.NotFound("support.ticket_not_found", "That ticket was not found.");
        ticket.Status = TicketStatus.Resolved;
        ticket.ResolutionNote = note;
        ticket.ResolvedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Envelope.Ok(TicketResponse.From(ticket));
    }

    private static async Task<(ProviderApplication Application, ProviderAccount Account)> FindAsync(
        GridDbContext db, Guid applicationId, CancellationToken ct)
    {
        var application = await db.ProviderApplications.SingleOrDefaultAsync(a => a.Id == applicationId, ct)
                          ?? throw Errors.NotFound("providers.application_not_found", "That application was not found.");
        return (application, await db.ProviderAccounts.SingleAsync(a => a.Id == application.ProviderAccountId, ct));
    }

    private static async Task<ServiceRequest> FindRequestAsync(GridDbContext db, Guid id, CancellationToken ct) =>
        await db.ServiceRequests.SingleOrDefaultAsync(r => r.Id == id, ct)
        ?? throw Errors.NotFound("services.request_not_found", "That booking was not found.");
}
