using System.Security.Cryptography;
using System.Text;
using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Auth;
using Grid.Api.Integrations.Sms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.ProviderLeads;

/// <summary>
/// Someone who applied to become a provider on the website, before (or while) they use GRID Pro.
/// One per phone. The account they later make is linked by <see cref="ProviderLeadLinker"/>.
/// </summary>
public sealed class ProviderLead : Entity
{
    /// <summary>Counts up from a sequence; shown as <c>L-1001</c>.</summary>
    public int Number { get; set; }
    public required string PhoneNumber { get; set; }
    public required string FullName { get; set; }

    /// <summary>Top-level catalogue slugs, e.g. <c>cleaning</c>.</summary>
    public List<string> Services { get; set; } = [];
    public required string Area { get; set; }

    /// <summary>One of <see cref="ProviderLeadEndpoints.ExperienceBands"/>.</summary>
    public required string Experience { get; set; }

    // First touch: kept from the first application that carried them.
    public string? Source { get; set; }
    public string? Medium { get; set; }
    public string? Campaign { get; set; }

    public DateTimeOffset ConsentAt { get; set; }
    public DateTimeOffset LastAppliedAt { get; set; }

    public DateTimeOffset? SmsSentAt { get; set; }
    public string? SmsProviderMessageId { get; set; }

    public Guid? ConsumerId { get; set; }
    public DateTimeOffset? SignedInAt { get; set; }
    public Guid? ProviderAccountId { get; set; }

    public DateTimeOffset? ContactedAt { get; set; }
    public Guid? ContactedByConsumerId { get; set; }
    public string? ContactNote { get; set; }

    public string Reference => $"L-{Number}";
}

/// <summary><c>Leads</c> config. With no <see cref="WebKey"/> the public endpoint refuses everything.</summary>
public sealed class LeadOptions
{
    public const string Section = "Leads";

    /// <summary>Shared with the website's server; sent as <c>X-Grid-Web-Key</c>.</summary>
    public string WebKey { get; set; } = "";

    /// <summary>Where the SMS sends people to get GRID Pro.</summary>
    public string ProDownloadUrl { get; set; } = "";

    public int PerIpPerHour { get; set; } = 20;
}

public sealed class LeadOptionsValidator : IValidateOptions<LeadOptions>
{
    public ValidateOptionsResult Validate(string? name, LeadOptions options)
    {
        var failures = new List<string>();
        if (options.PerIpPerHour < 1) failures.Add("Leads:PerIpPerHour must be at least 1.");
        if (!string.IsNullOrEmpty(options.WebKey))
        {
            if (options.WebKey.Length < 32) failures.Add("Leads:WebKey must be at least 32 characters.");
            if (!Uri.TryCreate(options.ProDownloadUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                failures.Add("Leads:ProDownloadUrl must be an https URL when Leads:WebKey is set.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public sealed record ProviderLeadRequest(
    string? FullName,
    string? PhoneNumber,
    List<string?>? Services,
    string? Area,
    string? Experience,
    string? Source,
    string? Medium,
    string? Campaign,
    bool? Consent);

public sealed record ProviderLeadResponse(string Reference, bool AlreadyRegistered);

public static class ProviderLeadEndpoints
{
    public const string RateLimitPolicy = "provider-leads";
    public const string WebKeyHeader = "X-Grid-Web-Key";
    public const string ClientIpHeader = "X-Grid-Client-Ip";
    public const string SmsPurpose = "ProviderLead";

    public static readonly string[] ExperienceBands = ["lt1", "1to3", "3to5", "5plus"];

    /// <summary>How long after one download text a re-application stays quiet.</summary>
    public static readonly TimeSpan SmsQuietPeriod = TimeSpan.FromHours(6);

    private const string OnePerPhoneIndex = "ix_provider_leads_phone_number";

    public static void MapProviderLeadEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/provider-leads", Apply)
            .WithTags("Provider leads")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy);
    }

    /// <summary>True when the request carries the website's key. Constant-time; no key configured means never.</summary>
    public static bool HasValidKey(HttpContext http, LeadOptions options)
    {
        if (string.IsNullOrEmpty(options.WebKey)) return false;
        var sent = http.Request.Headers[WebKeyHeader].ToString();
        return sent.Length > 0 && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(options.WebKey));
    }

    /// <summary>
    /// The rate-limit partition: the visitor's IP as the website reports it (trusted only with a
    /// valid key, since every website call comes from Vercel's servers), else the caller's own IP.
    /// </summary>
    public static string RateLimitKey(HttpContext http)
    {
        var options = http.RequestServices.GetRequiredService<IOptions<LeadOptions>>().Value;
        var reported = http.Request.Headers[ClientIpHeader].ToString().Trim();
        if (reported.Length is > 0 and <= 64 && HasValidKey(http, options)) return "client:" + reported;
        return "remote:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    /// <summary>
    /// Called by the website's server only. Creates or refreshes the lead for this phone and texts
    /// the GRID Pro download link (at most once per <see cref="SmsQuietPeriod"/>). A failed text
    /// doesn't fail the application; the next one tries again.
    /// </summary>
    private static async Task<EnvelopeResult<ProviderLeadResponse>> Apply(
        ProviderLeadRequest body,
        HttpContext http,
        GridDbContext db,
        SmsRouter sms,
        IOptions<LeadOptions> options,
        TimeProvider time,
        ILogger<ProviderLead> logger,
        CancellationToken ct)
    {
        var config = options.Value;
        if (!HasValidKey(http, config))
            throw new ApiException(401, "leads.unauthorized", "This endpoint is for the GRID website.");

        var launched = await db.ServiceCategories
            .Where(c => c.ParentCategoryId == null && c.IsActive && c.IsLaunched)
            .Select(c => c.Slug).ToListAsync(ct);

        var v = new Validator();
        var fullName = v.Required("fullName", body.FullName, 120, "Name");
        var phoneOk = PhoneNumbers.TryNormalize(body.PhoneNumber, out var phone);
        if (!phoneOk) v.Add("phoneNumber", "Enter a valid Ghana mobile number.");
        var services = (body.Services ?? []).Select(s => s?.Trim().ToLowerInvariant() ?? "").Distinct().ToList();
        if (services.Count == 0) v.Add("services", "Choose at least one service.");
        else if (services.Any(s => !launched.Contains(s))) v.Add("services", "Choose from the services GRID offers.");
        var area = v.Required("area", body.Area, 100, "Area");
        var experience = body.Experience?.Trim().ToLowerInvariant();
        if (experience is null || !ExperienceBands.Contains(experience)) v.Add("experience", "Choose your years of experience.");
        var source = v.Optional("source", body.Source, 100, "Source");
        var medium = v.Optional("medium", body.Medium, 100, "Medium");
        var campaign = v.Optional("campaign", body.Campaign, 100, "Campaign");
        if (body.Consent != true) v.Add("consent", "Agree to hear from GRID by SMS and WhatsApp.");
        v.ThrowIfInvalid();

        var now = time.GetUtcNow();
        var existing = await (
            from consumer in db.Consumers
            where consumer.PhoneNumber == phone
            select new { consumer.Id, AccountId = db.ProviderAccounts.Where(a => a.OwnerConsumerId == consumer.Id).Select(a => (Guid?)a.Id).FirstOrDefault() }
        ).SingleOrDefaultAsync(ct);
        var alreadyRegistered = existing?.AccountId is not null;

        var lead = await db.ProviderLeads.SingleOrDefaultAsync(l => l.PhoneNumber == phone, ct);
        var isNew = lead is null;
        lead ??= db.ProviderLeads.Add(new ProviderLead
        {
            PhoneNumber = phone, FullName = fullName, Area = area, Experience = experience!,
        }).Entity;

        lead.FullName = fullName;
        lead.Services = services;
        lead.Area = area;
        lead.Experience = experience!;
        lead.ConsentAt = now;
        lead.LastAppliedAt = now;
        if (lead.Source is null && lead.Medium is null && lead.Campaign is null)
        {
            lead.Source = source;
            lead.Medium = medium;
            lead.Campaign = campaign;
        }
        if (alreadyRegistered)
        {
            lead.ConsumerId ??= existing!.Id;
            lead.ProviderAccountId ??= existing!.AccountId;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (isNew && ex.IsUniqueViolation(OnePerPhoneIndex))
        {
            // The same phone applied twice at once; the other request made the lead and sent the text.
            db.ChangeTracker.Clear();
            var other = await db.ProviderLeads.AsNoTracking().SingleAsync(l => l.PhoneNumber == phone, ct);
            return Envelope.Ok(new ProviderLeadResponse(other.Reference, alreadyRegistered));
        }

        if (lead.SmsSentAt is null || now - lead.SmsSentAt >= SmsQuietPeriod)
        {
            var firstName = AuthEndpoints.SplitName(fullName)?.First ?? fullName;
            var result = await sms.SendAsync(new SmsMessage(phone,
                $"GRID Pro: thanks for applying, {firstName}. Get the app: {config.ProDownloadUrl}. " +
                "Sign in with this number to finish. GRID will never ask for your MoMo PIN.",
                SmsPurpose), ct);
            if (result.Accepted)
            {
                lead.SmsSentAt = now;
                lead.SmsProviderMessageId = result.ProviderMessageId;
                await db.SaveChangesAsync(ct);
            }
            else
            {
                logger.LogWarning("Download SMS for lead {Reference} was not sent: {ErrorCode}", lead.Reference, result.ErrorCode);
            }
        }

        return Envelope.Ok(new ProviderLeadResponse(lead.Reference, alreadyRegistered));
    }
}

/// <summary>
/// Joins a website lead to what the same phone does in GRID Pro, so Ops can follow the pipeline
/// without matching by hand. Never fails the sign-in or account creation it rides on.
/// </summary>
public sealed class ProviderLeadLinker(GridDbContext db, TimeProvider time, ILogger<ProviderLeadLinker> logger)
{
    /// <summary>After a GRID Pro sign-in: records who the lead is and when they first signed in.</summary>
    public async Task SignedInAsync(Guid consumerId, string phoneNumber, CancellationToken ct)
    {
        try
        {
            var now = time.GetUtcNow();
            var accountId = await db.ProviderAccounts.Where(a => a.OwnerConsumerId == consumerId)
                .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            await db.ProviderLeads
                .Where(l => l.PhoneNumber == phoneNumber && l.SignedInAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.ConsumerId, consumerId)
                    .SetProperty(l => l.SignedInAt, now)
                    .SetProperty(l => l.ProviderAccountId, l => l.ProviderAccountId ?? accountId)
                    .SetProperty(l => l.UpdatedAt, now), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not link a provider lead at sign-in");
        }
    }

    /// <summary>After a provider account is created.</summary>
    public async Task AccountCreatedAsync(Guid consumerId, string phoneNumber, Guid accountId, CancellationToken ct)
    {
        try
        {
            var now = time.GetUtcNow();
            await db.ProviderLeads
                .Where(l => l.PhoneNumber == phoneNumber && l.ProviderAccountId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.ConsumerId, consumerId)
                    .SetProperty(l => l.SignedInAt, l => l.SignedInAt ?? now)
                    .SetProperty(l => l.ProviderAccountId, accountId)
                    .SetProperty(l => l.UpdatedAt, now), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not link a provider lead to account {AccountId}", accountId);
        }
    }
}

/// <summary>How far a lead has got, derived from their account each time it is read.</summary>
public enum ProviderLeadStage { Applied, SignedIn, Onboarding, Submitted, Approved, Rejected, FirstJob }

public static class ProviderLeadStages
{
    /// <summary>The source shown and filtered on for leads that arrived without one.</summary>
    public const string Direct = "direct";

    public static ProviderLeadStage Of(ProviderLead lead, Providers.ApplicationStatus? application, DateTimeOffset? firstJobAt) =>
        firstJobAt is not null ? ProviderLeadStage.FirstJob
        : application == Providers.ApplicationStatus.Approved ? ProviderLeadStage.Approved
        : application == Providers.ApplicationStatus.Rejected ? ProviderLeadStage.Rejected
        : application is Providers.ApplicationStatus.Submitted or Providers.ApplicationStatus.UnderReview ? ProviderLeadStage.Submitted
        : lead.ProviderAccountId is not null ? ProviderLeadStage.Onboarding
        : lead.ConsumerId is not null ? ProviderLeadStage.SignedIn
        : ProviderLeadStage.Applied;
}
