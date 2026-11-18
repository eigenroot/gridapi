using System.Security.Cryptography;
using System.Text;
using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Catalogue;
using Grid.Api.Features.Consumers;
using Grid.Api.Features.Properties;
using Grid.Api.Features.ServicePayments;
using Grid.Api.Integrations.Hubtel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Marketplace;

/// <summary>
/// A booking: packages from one service, at one of the customer's homes, at a time. Send an
/// <c>Idempotency-Key</c> header; repeating it returns the booking already made.
/// </summary>
public sealed record CreateServiceRequest(
    List<PackageLineRequest>? Lines,
    Guid? PropertyId,
    DateTimeOffset? ScheduledAt,
    RequestUrgency? Urgency,
    List<string>? Items,
    string? Notes,
    string? Channel,
    Guid? PreferredProviderAccountId);

public sealed record CancelServiceRequest(string? Reason);

public sealed record PayServiceRequest(string? Channel);

public sealed record ReviewRequest(int? Rating, string? Comment);

/// <summary>Who is coming. Shared once a provider has accepted.</summary>
public sealed record AssignedProviderResponse(
    Guid Id, string DisplayName, string PhoneNumber, int CompletedJobs, double? Rating, int Reviews);

public sealed record RequestLineResponse(Guid PackageId, string Name, string? UnitLabel, decimal UnitPrice, int Quantity, decimal Total);

public sealed record RequestReviewResponse(int Rating, string? Comment, DateTimeOffset CreatedAt);

public sealed record ServiceRequestResponse(
    Guid Id,
    string Reference,
    string ServiceCategory,
    string ServiceName,
    BookingMode BookingMode,
    string Title,
    string? Scope,
    List<RequestLineResponse> Lines,
    decimal Total,
    List<string> Items,
    string? Notes,
    Guid? PropertyId,
    string Area,
    string City,
    string Address,
    DateTimeOffset ScheduledAt,
    RequestUrgency Urgency,
    ServiceRequestStatus Status,
    ServicePaymentResponse? Payment,
    string? JobCode,
    string? JobStage,
    AssignedProviderResponse? Provider,
    RequestReviewResponse? Review,
    bool CanCancel,
    DateTimeOffset CreatedAt);

/// <summary>One bookable hour on a day, and whether anyone nearby is working then.</summary>
public sealed record SlotResponse(DateTimeOffset StartsAt, bool Available, int Providers);

/// <summary>Whether GRID serves a home for a service, and when someone could come.</summary>
public sealed record ServiceAvailabilityResponse(bool Served, string? Area, int ProvidersNearby, List<SlotResponse> Slots);

/// <summary>The customer's side: book a service, pay, follow it, cancel it, confirm it was done, rate it.</summary>
public static class ServiceRequestEndpoints
{
    /// <summary>Visits start on the hour between these, Africa/Accra (UTC).</summary>
    private const int FirstSlotHour = 7, LastSlotHour = 17;

    public static void MapServiceRequestEndpoints(this IEndpointRouteBuilder api)
    {
        var requests = api.MapGroup("/service-requests").WithTags("Service requests").RequireAuthorization();

        requests.MapGet("/availability", Availability);
        requests.MapPost("", Create);
        requests.MapGet("", List);
        requests.MapGet("/{id:guid}", Get);
        requests.MapPost("/{id:guid}/pay", Pay);
        requests.MapPost("/{id:guid}/cancel", Cancel);
        requests.MapPost("/{id:guid}/confirm-completion", ConfirmCompletion);
        requests.MapPost("/{id:guid}/review", AddReview);
    }

    /// <summary>
    /// Whether a home is served for a service and, for one day, which hourly slots have a provider
    /// nearby who works then. <c>date</c> defaults to today (Africa/Accra).
    /// </summary>
    private static async Task<EnvelopeResult<ServiceAvailabilityResponse>> Availability(
        Guid? serviceCategoryId, Guid? propertyId, DateOnly? date,
        CurrentConsumer current, GridDbContext db, Dispatcher dispatcher, TimeProvider time,
        IOptions<MarketplaceOptions> options, CancellationToken ct)
    {
        var consumer = await current.LoadAsync(ct);
        var service = await db.ServiceCategories.SingleOrDefaultAsync(
                          c => c.Id == serviceCategoryId && c.ParentCategoryId == null && c.IsActive, ct)
                      ?? throw Errors.NotFound("catalogue.category_not_found", "That service was not found.");
        var property = await PropertyEndpoints.FindOwnedAsync(db, consumer.Id, propertyId ?? Guid.Empty, ct);
        var areaId = property.ServiceAreaId ?? await PropertyEndpoints.ResolveAreaAsync(db, property, ct);
        if (areaId is null) return Envelope.Ok(new ServiceAvailabilityResponse(false, null, 0, []));

        var now = time.GetUtcNow();
        var day = date ?? DateOnly.FromDateTime(now.UtcDateTime);
        var earliest = now.AddMinutes(options.Value.MinLeadMinutes);
        var slots = new List<SlotResponse>();
        var most = 0;
        for (var hour = FirstSlotHour; hour <= LastSlotHour; hour++)
        {
            var startsAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
            if (startsAt < earliest) continue;
            var probe = Probe(consumer.Id, service, property, areaId.Value, startsAt);
            var count = (await dispatcher.MatchAsync(probe, ct)).Count;
            most = Math.Max(most, count);
            slots.Add(new SlotResponse(startsAt, count > 0, count));
        }
        var area = await db.ServiceAreas.Where(a => a.Id == areaId).Select(a => a.Name).SingleAsync(ct);
        return Envelope.Ok(new ServiceAvailabilityResponse(true, area, most, slots));
    }

    private static ServiceRequest Probe(Guid consumerId, ServiceCategory service, Property property, Guid areaId, DateTimeOffset at) => new()
    {
        CustomerConsumerId = consumerId, ServiceCategoryId = service.Id, Title = service.Name,
        CustomerName = "", Area = property.Area, City = property.City, Address = property.Address,
        Latitude = property.Latitude, Longitude = property.Longitude, ServiceAreaId = areaId, ScheduledAt = at,
    };

    /// <summary>
    /// Books packages at one of the customer's homes, priced by GRID, and prompts their MoMo for
    /// the total. The nearest providers are offered it once the payment is approved.
    /// </summary>
    private static async Task<EnvelopeResult<ServiceRequestResponse>> Create(
        CreateServiceRequest body, HttpContext http, CurrentConsumer current, GridDbContext db,
        ServicePaymentService payments, Dispatcher dispatcher, IOptions<MarketplaceOptions> options,
        TimeProvider time, CancellationToken ct)
    {
        var config = options.Value;
        if (!config.Enabled)
            throw new ApiException(503, "services.disabled", "Booking isn't open right now. Try again shortly.");

        var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new ApiException(400, "payments.idempotency_required", "Send an Idempotency-Key header with each booking.");
        var keyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        var consumer = await current.LoadAsync(ct);
        var replay = await db.ServiceRequests.SingleOrDefaultAsync(
            r => r.CustomerConsumerId == consumer.Id && r.IdempotencyKeyHash == keyHash, ct);
        if (replay is not null) return Envelope.Ok(await ResponseAsync(db, replay.Id, ct));

        var now = time.GetUtcNow();
        var v = new Validator();
        if (body.PropertyId is null) v.Add("propertyId", "Choose where the service is needed.");
        if (body.ScheduledAt is not { } scheduledAt) v.Add("scheduledAt", "Choose a day and time.");
        else if (scheduledAt < now.AddMinutes(config.MinLeadMinutes)) v.Add("scheduledAt", "Choose a later time.");
        else if (scheduledAt > now.AddDays(config.MaxLeadDays)) v.Add("scheduledAt", $"Book up to {config.MaxLeadDays} days ahead.");
        var notes = v.Optional("notes", body.Notes, 1000, "Notes");
        var items = (body.Items ?? []).Select(i => i?.Trim() ?? "").Where(i => i.Length > 0).ToList();
        if (items.Count > 50 || items.Any(i => i.Length > 100)) v.Add("items", "List up to 50 items, 100 characters each.");
        var channel = body.Channel ?? HubtelPayments.SuggestChannel(consumer.PhoneNumber);
        if (HubtelPayments.Channels.All(c => c.Code != channel)) v.Add("channel", "Choose your mobile money network.");
        v.ThrowIfInvalid();

        var priced = await ServicePricing.QuoteAsync(db, body.Lines, ct);
        var property = await PropertyEndpoints.FindOwnedAsync(db, consumer.Id, body.PropertyId!.Value, ct);
        var areaId = property.ServiceAreaId ?? await PropertyEndpoints.ResolveAreaAsync(db, property, ct);
        if (areaId is null)
        {
            throw new ApiException(422, "services.area_not_served", "GRID doesn't serve this area yet.",
                fields: [new FieldError("propertyId", "GRID doesn't serve this area yet.")]);
        }

        var request = new ServiceRequest
        {
            CustomerConsumerId = consumer.Id,
            PropertyId = property.Id,
            ServiceCategoryId = priced.Service.Id,
            Title = priced.Title,
            Scope = priced.Scope,
            Items = items,
            Notes = notes,
            Total = priced.Total,
            ScheduledAt = body.ScheduledAt!.Value,
            Urgency = body.Urgency ?? RequestUrgency.Standard,
            CustomerName = consumer.FullName.Length > 0 ? consumer.FullName : "GRID customer",
            Area = property.Area,
            City = property.City,
            Address = property.Address,
            GhanaPostGps = property.GhanaPostGps,
            Latitude = property.Latitude,
            Longitude = property.Longitude,
            ServiceAreaId = areaId,
            PreferredProviderAccountId = body.PreferredProviderAccountId,
            IdempotencyKeyHash = keyHash,
            Status = ServiceRequestStatus.AwaitingPayment,
        };
        db.ServiceRequests.Add(request);
        foreach (var line in priced.Lines)
        {
            line.ServiceRequestId = request.Id;
            db.ServiceRequestLines.Add(line);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_service_requests_one_per_key"))
        {
            db.ChangeTracker.Clear();
            var raced = await db.ServiceRequests.SingleAsync(
                r => r.CustomerConsumerId == consumer.Id && r.IdempotencyKeyHash == keyHash, ct);
            return Envelope.Ok(await ResponseAsync(db, raced.Id, ct));
        }

        await payments.StartAsync(request, consumer, channel!, ct);
        // A simulated payment is approved at once; don't make the customer wait for the worker.
        if (await db.ServiceRequests.AnyAsync(r => r.Id == request.Id && r.Status == ServiceRequestStatus.Searching, ct))
            await dispatcher.DispatchAsync(request.Id, ct);
        return Envelope.Created(await ResponseAsync(db, request.Id, CancellationToken.None));
    }

    /// <summary>Prompts the wallet again for a booking whose payment failed or was never approved.</summary>
    private static async Task<EnvelopeResult<ServiceRequestResponse>> Pay(
        Guid id, HttpRequest http, CurrentConsumer current, GridDbContext db, ServicePaymentService payments,
        Dispatcher dispatcher, CancellationToken ct)
    {
        var body = await OptionalBody.ReadAsync<PayServiceRequest>(http, ct);
        var consumer = await current.LoadAsync(ct);
        var request = await FindOwnedAsync(db, consumer.Id, id, ct);
        if (request.Status != ServiceRequestStatus.PaymentFailed)
            throw Errors.Conflict("services.payment_not_retryable", "This booking isn't waiting for a new payment.");
        var channel = body?.Channel ?? HubtelPayments.SuggestChannel(consumer.PhoneNumber);
        if (HubtelPayments.Channels.All(c => c.Code != channel))
            throw Errors.Validation([new FieldError("channel", "Choose your mobile money network.")]);

        request.Status = ServiceRequestStatus.AwaitingPayment;
        await db.SaveChangesAsync(ct);
        await payments.StartAsync(request, consumer, channel!, ct);
        if (await db.ServiceRequests.AnyAsync(r => r.Id == request.Id && r.Status == ServiceRequestStatus.Searching, ct))
            await dispatcher.DispatchAsync(request.Id, ct);
        return Envelope.Ok(await ResponseAsync(db, request.Id, CancellationToken.None));
    }

    private static async Task<EnvelopeResult<List<ServiceRequestResponse>>> List(
        CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var consumerId = (await current.LoadAsync(ct)).Id;
        var ids = await db.ServiceRequests.Where(r => r.CustomerConsumerId == consumerId)
            .OrderByDescending(r => r.CreatedAt).Select(r => r.Id).Take(50).ToListAsync(ct);
        var rows = new List<ServiceRequestResponse>();
        foreach (var id in ids) rows.Add(await ResponseAsync(db, id, ct));
        return Envelope.Ok(rows);
    }

    private static async Task<EnvelopeResult<ServiceRequestResponse>> Get(
        Guid id, CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var request = await FindOwnedAsync(db, (await current.LoadAsync(ct)).Id, id, ct);
        return Envelope.Ok(await ResponseAsync(db, request.Id, ct));
    }

    /// <summary>Calls a booking off before the provider sets off. Anything paid is refunded.</summary>
    private static async Task<EnvelopeResult<ServiceRequestResponse>> Cancel(
        Guid id, HttpRequest http, CurrentConsumer current, GridDbContext db, JobService jobs, CancellationToken ct)
    {
        var body = await OptionalBody.ReadAsync<CancelServiceRequest>(http, ct);
        var v = new Validator();
        var reason = v.Optional("reason", body?.Reason, 300, "Reason");
        v.ThrowIfInvalid();
        var consumerId = (await current.LoadAsync(ct)).Id;
        var request = await FindOwnedAsync(db, consumerId, id, ct);
        await jobs.CancelRequestAsync(request, consumerId, reason, ct);
        return Envelope.Ok(await ResponseAsync(db, request.Id, ct));
    }

    /// <summary>The customer agreeing the work is done, which releases the provider's earning.</summary>
    private static async Task<EnvelopeResult<ServiceRequestResponse>> ConfirmCompletion(
        Guid id, CurrentConsumer current, GridDbContext db, JobService jobs, CancellationToken ct)
    {
        var consumerId = (await current.LoadAsync(ct)).Id;
        var request = await FindOwnedAsync(db, consumerId, id, ct);
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.ServiceRequestId == request.Id && j.Stage != JobStage.Cancelled, ct)
                  ?? throw new ApiException(409, "jobs.invalid_transition", "This booking has no job to confirm yet.");
        await jobs.ConfirmAsync(job, consumerId, ct);
        return Envelope.Ok(await ResponseAsync(db, request.Id, ct));
    }

    /// <summary>Rates a finished job, once. The rating is the provider's; the comment is for GRID and the provider.</summary>
    private static async Task<EnvelopeResult<ServiceRequestResponse>> AddReview(
        Guid id, ReviewRequest body, CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var v = new Validator();
        if (body.Rating is not (>= 1 and <= 5)) v.Add("rating", "Choose 1 to 5 stars.");
        var comment = v.Optional("comment", body.Comment, 1000, "Comment");
        v.ThrowIfInvalid();

        var consumerId = (await current.LoadAsync(ct)).Id;
        var request = await FindOwnedAsync(db, consumerId, id, ct);
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.ServiceRequestId == request.Id && j.Stage == JobStage.Completed, ct)
                  ?? throw Errors.Conflict("reviews.not_completed", "You can rate a job once it's done.");
        if (await db.Reviews.AnyAsync(r => r.JobId == job.Id, ct))
            throw Errors.Conflict("reviews.already_reviewed", "You've already rated this job.");

        db.Reviews.Add(new Review
        {
            JobId = job.Id, ServiceRequestId = request.Id, ProviderAccountId = job.ProviderAccountId,
            CustomerConsumerId = consumerId, Rating = body.Rating!.Value, Comment = comment,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw Errors.Conflict("reviews.already_reviewed", "You've already rated this job.");
        }
        return Envelope.Ok(await ResponseAsync(db, request.Id, ct));
    }

    private static async Task<ServiceRequest> FindOwnedAsync(GridDbContext db, Guid consumerId, Guid id, CancellationToken ct) =>
        await db.ServiceRequests.SingleOrDefaultAsync(r => r.Id == id && r.CustomerConsumerId == consumerId, ct)
        ?? throw Errors.NotFound("services.request_not_found", "That booking was not found.");

    /// <summary>A provider's average rating, or null before their first review.</summary>
    public static async Task<(double? Rating, int Count)> RatingAsync(GridDbContext db, Guid providerAccountId, CancellationToken ct)
    {
        var ratings = await db.Reviews.Where(r => r.ProviderAccountId == providerAccountId).Select(r => r.Rating).ToListAsync(ct);
        return ratings.Count == 0 ? (null, 0) : (Math.Round(ratings.Average(), 1), ratings.Count);
    }

    internal static async Task<ServiceRequestResponse> ResponseAsync(GridDbContext db, Guid requestId, CancellationToken ct)
    {
        var request = await db.ServiceRequests.AsNoTracking().SingleAsync(r => r.Id == requestId, ct);
        var service = await db.ServiceCategories.AsNoTracking().SingleAsync(c => c.Id == request.ServiceCategoryId, ct);
        var lines = await db.ServiceRequestLines.AsNoTracking().Where(l => l.ServiceRequestId == requestId)
            .OrderBy(l => l.SortOrder)
            .Select(l => new RequestLineResponse(l.ServicePackageId, l.Name, l.UnitLabel, l.UnitPrice, l.Quantity, l.Total))
            .ToListAsync(ct);
        var payment = (await ServicePaymentService.LatestAsync(db, [requestId], ct)).GetValueOrDefault(requestId);
        var job = await db.Jobs.AsNoTracking()
            .Where(j => j.ServiceRequestId == requestId && j.Stage != JobStage.Cancelled)
            .SingleOrDefaultAsync(ct);

        AssignedProviderResponse? provider = null;
        RequestReviewResponse? review = null;
        if (job is not null)
        {
            var row = await (
                from account in db.ProviderAccounts
                join owner in db.Consumers on account.OwnerConsumerId equals owner.Id
                where account.Id == job.ProviderAccountId
                select new { account.Id, account.DisplayName, owner.PhoneNumber }).SingleAsync(ct);
            var done = await db.Jobs.CountAsync(j => j.ProviderAccountId == row.Id && j.Stage == JobStage.Completed, ct);
            var (rating, count) = await RatingAsync(db, row.Id, ct);
            provider = new AssignedProviderResponse(row.Id, row.DisplayName, row.PhoneNumber, done, rating, count);
            review = await db.Reviews.AsNoTracking().Where(r => r.JobId == job.Id)
                .Select(r => new RequestReviewResponse(r.Rating, r.Comment, r.CreatedAt)).SingleOrDefaultAsync(ct);
        }

        var canCancel = request.Status is ServiceRequestStatus.AwaitingPayment or ServiceRequestStatus.PaymentFailed
            or ServiceRequestStatus.Searching or ServiceRequestStatus.Assigned or ServiceRequestStatus.Unfilled
            && (job is null || job.Stage == JobStage.Accepted);

        return new ServiceRequestResponse(
            request.Id, request.Reference, service.Slug, service.Name, service.BookingMode, request.Title, request.Scope, lines,
            request.Total, request.Items, request.Notes, request.PropertyId, request.Area, request.City, request.Address,
            request.ScheduledAt, request.Urgency, request.Status, payment is null ? null : ServicePaymentResponse.From(payment),
            job?.Code, job is null ? null : JobService.Stage(job.Stage), provider, review, canCancel, request.CreatedAt);
    }
}
