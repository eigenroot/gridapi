using System.Text.Json;
using System.Text.Json.Serialization;
using Grid.Api.Common;
using Grid.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Providers;

public sealed record ApplicationResponse(
    Guid Id,
    Guid ProviderAccountId,
    int Version,
    ApplicationStatus Status,
    int CurrentStep,
    string? MainSkill,
    string? SecondarySkills,
    string? ToolsNotes,
    string? Certifications,
    string? ExperienceDescription,
    int? YearsOperating,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewNote,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ApplicationResponse From(ProviderApplication a) =>
        new(a.Id, a.ProviderAccountId, a.Version, a.Status, a.CurrentStep, a.MainSkill, a.SecondarySkills,
            a.ToolsNotes, a.Certifications, a.ExperienceDescription, a.YearsOperating, a.SubmittedAt,
            a.ReviewedAt, a.ReviewNote, a.RejectionReason, a.CreatedAt, a.UpdatedAt);
}

public sealed record ExperienceRequest(
    string? MainSkill,
    string? SecondarySkills,
    string? ToolsNotes,
    string? Certifications,
    string? ExperienceDescription,
    int? YearsOperating);

public sealed record CurrentStepRequest(int? Step);

public static class ProviderApplicationEndpoints
{
    private const string OneSubmissionPerVersionIndex = "ix_provider_application_submissions_one_per_version";

    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static void MapProviderApplicationEndpoints(this IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("/provider-applications/me").WithTags("Provider onboarding").RequireAuthorization();

        me.MapGet("", Get);
        me.MapPut("/experience", UpdateExperience);
        me.MapPut("/current-step", SetCurrentStep);
        me.MapGet("/readiness", Readiness);
        me.MapPost("/submit", Submit);
    }

    private static async Task<EnvelopeResult<ApplicationResponse>> Get(CurrentProvider provider, CancellationToken ct) =>
        Envelope.Ok(ApplicationResponse.From((await provider.LoadAsync(ct)).Application));

    /// <summary>Full replace.</summary>
    private static async Task<EnvelopeResult<ApplicationResponse>> UpdateExperience(
        ExperienceRequest body, CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var v = new Validator();
        var mainSkill = v.Optional("mainSkill", body.MainSkill, 100, "Main skill");
        var secondary = v.Optional("secondarySkills", body.SecondarySkills, 1000, "Other skills");
        var tools = v.Optional("toolsNotes", body.ToolsNotes, 2000, "Tools and equipment");
        var certifications = v.Optional("certifications", body.Certifications, 2000, "Certifications");
        var description = v.Optional("experienceDescription", body.ExperienceDescription, 4000, "Experience");
        if (body.YearsOperating is < 0 or > 80) v.Add("yearsOperating", "Enter your years of experience.");
        v.ThrowIfInvalid();

        var application = (await provider.LoadEditableAsync(ct)).Application;
        application.MainSkill = mainSkill;
        application.SecondarySkills = secondary;
        application.ToolsNotes = tools;
        application.Certifications = certifications;
        application.ExperienceDescription = description;
        application.YearsOperating = body.YearsOperating;
        await db.SaveChangesAsync(ct);

        return Envelope.Ok(ApplicationResponse.From(application));
    }

    /// <summary>Progress through the app's flow; never locked, since it changes nothing GRID reviews.</summary>
    private static async Task<EnvelopeResult<ApplicationResponse>> SetCurrentStep(
        CurrentStepRequest body, CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        if (body.Step is not (>= 0 and <= 50))
            throw Errors.Validation([new FieldError("step", "Step must be between 0 and 50.")]);

        var application = (await provider.LoadAsync(ct)).Application;
        application.CurrentStep = body.Step.Value;
        await db.SaveChangesAsync(ct);
        return Envelope.Ok(ApplicationResponse.From(application));
    }

    private static async Task<EnvelopeResult<ReadinessResponse>> Readiness(
        CurrentProvider provider, GridDbContext db, CancellationToken ct) =>
        Envelope.Ok(await ProviderReadiness.EvaluateAsync(db, (await provider.LoadAsync(ct)).Account, ct));

    /// <summary>
    /// Sends a draft (or a corrected application) to GRID for review. Repeating a submit that
    /// already went through returns the submitted application, so a retry after a lost response is safe.
    /// </summary>
    private static async Task<EnvelopeResult<ApplicationResponse>> Submit(
        CurrentProvider provider, GridDbContext db, TimeProvider time, CancellationToken ct)
    {
        var (_, account, application) = await provider.LoadAsync(ct);
        if (application.Status == ApplicationStatus.Submitted) return Envelope.Ok(ApplicationResponse.From(application));
        if (application.Status is not (ApplicationStatus.Draft or ApplicationStatus.MoreInformationRequired))
            throw Errors.Conflict("providers.application_not_submittable", "This application can't be submitted again.");

        var readiness = await ProviderReadiness.EvaluateAsync(db, account, ct);
        if (!readiness.IsReady)
        {
            throw new ApiException(422, "providers.application_incomplete", "Your application isn't complete yet.",
                new { readiness.MissingRequirements });
        }

        var now = time.GetUtcNow();
        if (application.SubmittedAt is not null) application.Version++;
        application.Status = ApplicationStatus.Submitted;
        application.SubmittedAt = now;
        account.VerificationStatus = ProviderVerificationStatus.Submitted;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.ProviderApplicationSubmissions.Add(new ProviderApplicationSubmission
        {
            ProviderApplicationId = application.Id,
            Version = application.Version,
            Snapshot = await SnapshotAsync(db, account, application, ct),
            SubmittedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(OneSubmissionPerVersionIndex))
        {
            // A concurrent submit of the same version won; report what it saved.
            db.ChangeTracker.Clear();
            application = await db.ProviderApplications.SingleAsync(a => a.Id == application.Id, ct);
            return Envelope.Ok(ApplicationResponse.From(application));
        }
        await tx.CommitAsync(ct);

        return Envelope.Ok(ApplicationResponse.From(application));
    }

    /// <summary>The application as GRID will review it, frozen at submission.</summary>
    private static async Task<string> SnapshotAsync(
        GridDbContext db, ProviderAccount account, ProviderApplication application, CancellationToken ct)
    {
        var id = account.Id;
        var snapshot = new
        {
            account = ProviderAccountResponse.From(account),
            experience = new
            {
                application.MainSkill, application.SecondarySkills, application.ToolsNotes,
                application.Certifications, application.ExperienceDescription, application.YearsOperating,
            },
            offerings = await db.ProviderOfferings.Where(o => o.ProviderAccountId == id && o.IsActive)
                .Select(o => OfferingResponse.From(o)).ToListAsync(ct),
            serviceAreas = await db.ProviderServiceAreas.Where(a => a.ProviderAccountId == id)
                .Select(a => a.ServiceAreaId).ToListAsync(ct),
            availability = await db.ProviderAvailabilityRules.Where(r => r.ProviderAccountId == id)
                .Select(r => AvailabilityRuleResponse.From(r)).ToListAsync(ct),
            documents = await db.VerificationDocuments.Where(d => d.ProviderAccountId == id && d.SupersededAt == null)
                .Select(d => new { d.Id, d.DocumentType, d.StorageKey }).ToListAsync(ct),
            standardsVersion = ProviderStandards.CurrentVersion,
        };
        return JsonSerializer.Serialize(snapshot, SnapshotJson);
    }
}
