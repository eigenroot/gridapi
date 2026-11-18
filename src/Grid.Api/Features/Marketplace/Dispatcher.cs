using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Catalogue;
using Grid.Api.Features.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Marketplace;

/// <summary>A provider who could take a request, with what they would charge and how far away they are.</summary>
public sealed record Candidate(Guid ProviderAccountId, double DistanceKm, bool CoversArea, int ActiveJobs);

/// <summary>
/// Finds the nearest providers for a request and offers it to them a few at a time.
///
/// A provider is eligible when they are approved, online, offer the service, work at
/// that day and time, and can reach the job: either they listed the job's area, or the job is
/// within their travel radius. Eligible providers are ranked by straight-line distance from their
/// base (their shared location, or else the centre of the nearest area they cover), then by how
/// little they already have on.
/// </summary>
public sealed class Dispatcher(
    GridDbContext db,
    IOptions<MarketplaceOptions> options,
    TimeProvider time,
    ServicePayments.ServicePaymentService payments,
    ILogger<Dispatcher> logger)
{
    private const string OneOfferPerProviderIndex = "ix_job_offers_service_request_id_provider_account_id";

    /// <summary>Sends the next round of offers if the request is still looking and none are open. Returns how many went out.</summary>
    public async Task<int> DispatchAsync(Guid requestId, CancellationToken ct)
    {
        var config = options.Value;
        var now = time.GetUtcNow();
        var request = await db.ServiceRequests.SingleOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || request.Status != ServiceRequestStatus.Searching) return 0;
        if (await db.JobOffers.AnyAsync(
                o => o.ServiceRequestId == requestId && o.Status == OfferStatus.Pending && o.ExpiresAt > now, ct))
            return 0;

        var candidates = await MatchAsync(request, ct);
        // "Book Ama again": the first round goes to that provider alone, if they can take it.
        var preferred = request.DispatchRound == 0 && request.PreferredProviderAccountId is { } wanted
            ? candidates.Where(c => c.ProviderAccountId == wanted).ToList()
            : [];
        var chosen = preferred.Count > 0 ? preferred : candidates.Take(config.OffersPerRound).ToList();
        request.DispatchRound++;
        foreach (var candidate in chosen)
        {
            db.JobOffers.Add(new JobOffer
            {
                ServiceRequestId = request.Id,
                ProviderAccountId = candidate.ProviderAccountId,
                Round = request.DispatchRound,
                Price = request.Total,
                DistanceKm = (decimal)Math.Round(candidate.DistanceKm, 1),
                ExpiresAt = now.AddMinutes(config.OfferMinutes),
            });
        }

        if (chosen.Count > 0)
        {
            request.NextDispatchAt = now.AddMinutes(config.OfferMinutes);
        }
        else if (request.DispatchRound >= config.MaxRounds)
        {
            request.Status = ServiceRequestStatus.Unfilled;
            request.NextDispatchAt = null;
            await payments.RequestRefundAsync(request.Id, "Unfilled", ct);
        }
        else
        {
            request.NextDispatchAt = now.AddMinutes(config.RetryMinutes);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(OneOfferPerProviderIndex))
        {
            // Another dispatch of the same request got there first; its offers stand.
            db.ChangeTracker.Clear();
            return 0;
        }

        logger.LogInformation(
            "Request {Reference} round {Round}: {Offers} offers, status {Status}",
            request.Reference, request.DispatchRound, chosen.Count, request.Status);
        return chosen.Count;
    }

    /// <summary>Everyone who could take the request and hasn't been offered it, nearest first.</summary>
    public async Task<List<Candidate>> MatchAsync(ServiceRequest request, CancellationToken ct)
    {
        var config = options.Value;
        var area = request.ServiceAreaId is { } areaId
            ? await db.ServiceAreas.SingleOrDefaultAsync(a => a.Id == areaId, ct)
            : null;
        var point = GeoPoint.From(request.Latitude, request.Longitude) ?? area?.Centre;
        if (point is null) return [];

        var rows = await (
            from account in db.ProviderAccounts
            join offering in db.ProviderOfferings on account.Id equals offering.ProviderAccountId
            where account.VerificationStatus == ProviderVerificationStatus.Approved
                  && account.OperatingStatus == OperatingStatus.Online
                  && account.OwnerConsumerId != request.CustomerConsumerId
                  && offering.ServiceCategoryId == request.ServiceCategoryId
                  && offering.IsActive
                  && !db.JobOffers.Any(o => o.ServiceRequestId == request.Id && o.ProviderAccountId == account.Id)
            select new { account, offering }).ToListAsync(ct);
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.account.Id).ToList();
        var covered = await (
            from row in db.ProviderServiceAreas
            join a in db.ServiceAreas on row.ServiceAreaId equals a.Id
            where ids.Contains(row.ProviderAccountId)
            select new { row.ProviderAccountId, row.MaximumTravelDistanceKm, Area = a }).ToListAsync(ct);

        // Africa/Accra is UTC all year, so the UTC day and time are the local ones.
        var when = request.ScheduledAt.UtcDateTime;
        var day = when.DayOfWeek;
        var at = TimeOnly.FromDateTime(when);
        var working = (await db.ProviderAvailabilityRules
            .Where(r => ids.Contains(r.ProviderAccountId) && r.DayOfWeek == day && r.IsAvailable
                        && r.StartTime <= at && r.EndTime > at)
            .Select(r => r.ProviderAccountId)
            .ToListAsync(ct)).ToHashSet();

        var active = await db.Jobs
            .Where(j => ids.Contains(j.ProviderAccountId)
                        && j.Stage != JobStage.AwaitingConfirmation && j.Stage != JobStage.Completed && j.Stage != JobStage.Cancelled)
            .Select(j => new { j.ProviderAccountId, j.Workflow, j.ScheduledAt })
            .ToListAsync(ct);
        var gap = TimeSpan.FromMinutes(config.VisitGapMinutes);

        var candidates = new List<Candidate>();
        foreach (var row in rows)
        {
            var account = row.account;
            if (!working.Contains(account.Id)) continue;
            if (request.Urgency == RequestUrgency.SameDay && !row.offering.SupportsSameDay) continue;

            var mine = active.Where(j => j.ProviderAccountId == account.Id).ToList();
            // Two visits can't overlap; laundry is worked on between other jobs, so it never blocks.
            if (mine.Any(j => j.Workflow == JobWorkflow.ScheduledService && (j.ScheduledAt - request.ScheduledAt).Duration() < gap))
                continue;

            var areas = covered.Where(c => c.ProviderAccountId == account.Id).ToList();
            var coversArea = area is not null && areas.Any(c => c.Area.Id == area.Id);
            var basePoint = GeoPoint.From(account.BaseLatitude, account.BaseLongitude);
            double? distance = basePoint is { } from
                ? Geo.DistanceKm(from, point.Value)
                : areas.Count > 0 ? areas.Min(c => Geo.DistanceKm(c.Area.Centre, point.Value)) : null;
            if (distance is null) continue;

            var radius = (double)(account.TravelRadiusKm
                                  ?? areas.Max(c => c.MaximumTravelDistanceKm)
                                  ?? config.DefaultTravelRadiusKm);
            if (!coversArea && distance > radius) continue;

            candidates.Add(new Candidate(account.Id, distance.Value, coversArea, mine.Count));
        }

        return candidates.OrderBy(c => c.DistanceKm).ThenBy(c => c.ActiveJobs).ThenBy(c => c.ProviderAccountId).ToList();
    }
}
