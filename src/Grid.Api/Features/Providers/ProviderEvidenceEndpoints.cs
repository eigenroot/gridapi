using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Media;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Providers;

public sealed record PortfolioItemRequest(
    Guid? ServiceCategoryId,
    string? Title,
    string? Description,
    string? StorageKey,
    string? ContentType,
    DateOnly? ProjectCompletedAt);

public sealed record PortfolioItemResponse(
    Guid Id,
    Guid? ServiceCategoryId,
    string Title,
    string? Description,
    string StorageKey,
    string ContentType,
    DateOnly? ProjectCompletedAt,
    DateTimeOffset CreatedAt)
{
    public static PortfolioItemResponse From(PortfolioItem p) =>
        new(p.Id, p.ServiceCategoryId, p.Title, p.Description, p.StorageKey, p.ContentType, p.ProjectCompletedAt, p.CreatedAt);
}

/// <summary>File name, type and size are taken from the confirmed upload; the request's copies are ignored.</summary>
public sealed record VerificationDocumentRequest(
    VerificationDocumentType? DocumentType,
    string? StorageKey,
    string? FileName,
    string? ContentType,
    long? SizeBytes);

public sealed record VerificationDocumentResponse(
    Guid Id,
    VerificationDocumentType DocumentType,
    string StorageKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    ReviewStatus ReviewStatus,
    string? ReviewNote,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset CreatedAt)
{
    public static VerificationDocumentResponse From(VerificationDocument d) =>
        new(d.Id, d.DocumentType, d.StorageKey, d.FileName, d.ContentType, d.SizeBytes, d.ReviewStatus,
            d.ReviewNote, d.ReviewedAt, d.CreatedAt);
}

public sealed record StandardsResponse(string Version, bool Accepted, DateTimeOffset? AcceptedAt);

/// <summary>Portfolio, verification documents and standards: the evidence GRID reviews.</summary>
public static class ProviderEvidenceEndpoints
{
    private const string OneAcceptancePerVersionIndex = "ix_provider_standards_acceptances_one_per_version";

    private static readonly MediaPurpose[] IdentityPurposes = [MediaPurpose.Identity, MediaPurpose.ProfilePhoto];
    private static readonly MediaPurpose[] CertificatePurposes = [MediaPurpose.Certificates];

    public static void MapProviderEvidenceEndpoints(this IEndpointRouteBuilder api)
    {
        var portfolio = api.MapGroup("/provider-portfolio").WithTags("Provider onboarding").RequireAuthorization();
        portfolio.MapGet("", ListPortfolio);
        portfolio.MapPost("", AddPortfolioItem);
        portfolio.MapDelete("/{id:guid}", ArchivePortfolioItem);

        var documents = api.MapGroup("/provider-verification-documents").WithTags("Provider onboarding").RequireAuthorization();
        documents.MapGet("", ListDocuments);
        documents.MapPost("", AddDocument);

        var standards = api.MapGroup("/provider-standards/current").WithTags("Provider onboarding").RequireAuthorization();
        standards.MapGet("", GetStandards);
        standards.MapPost("/accept", AcceptStandards);
    }

    // ── Portfolio ──────────────────────────────────────────────────────────

    private static async Task<EnvelopeResult<List<PortfolioItemResponse>>> ListPortfolio(
        CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var rows = await db.PortfolioItems.Where(p => p.ProviderAccountId == accountId && p.ArchivedAt == null)
            .OrderBy(p => p.CreatedAt).ToListAsync(ct);
        return Envelope.Ok(rows.Select(PortfolioItemResponse.From).ToList());
    }

    private static async Task<EnvelopeResult<PortfolioItemResponse>> AddPortfolioItem(
        PortfolioItemRequest body, CurrentProvider provider, GridDbContext db, TimeProvider time, CancellationToken ct)
    {
        var v = new Validator();
        var title = v.Required("title", body.Title, 120, "Title");
        var description = v.Optional("description", body.Description, 1000, "Description");
        v.ThrowIfInvalid();

        var (consumer, account, _) = await provider.LoadAsync(ct);
        if (body.ServiceCategoryId is { } categoryId && !await db.ServiceCategories.AnyAsync(c => c.Id == categoryId, ct))
            throw Errors.NotFound("catalogue.category_not_found", "That service was not found.");

        var media = await MediaEndpoints.FindAttachableAsync(db, consumer.Id, body.StorageKey, [MediaPurpose.Portfolio], ct);
        media.AttachedAt = time.GetUtcNow();
        var item = new PortfolioItem
        {
            ProviderAccountId = account.Id,
            ServiceCategoryId = body.ServiceCategoryId,
            Title = title,
            Description = description,
            MediaObjectId = media.Id,
            StorageKey = media.StorageKey,
            ContentType = media.ContentType,
            ProjectCompletedAt = body.ProjectCompletedAt,
        };
        db.PortfolioItems.Add(item);
        await db.SaveChangesAsync(ct);
        return Envelope.Created(PortfolioItemResponse.From(item));
    }

    private static async Task<EnvelopeResult<object>> ArchivePortfolioItem(
        Guid id, CurrentProvider provider, GridDbContext db, TimeProvider time, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var item = await db.PortfolioItems.SingleOrDefaultAsync(p => p.Id == id && p.ProviderAccountId == accountId && p.ArchivedAt == null, ct)
                   ?? throw Errors.NotFound("providers.portfolio_item_not_found", "That work photo was not found.");
        item.ArchivedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Envelope.Empty();
    }

    // ── Verification documents ─────────────────────────────────────────────

    /// <summary>Current documents only; superseded copies are kept for review history.</summary>
    private static async Task<EnvelopeResult<List<VerificationDocumentResponse>>> ListDocuments(
        CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var rows = await db.VerificationDocuments.Where(d => d.ProviderAccountId == accountId && d.SupersededAt == null)
            .OrderBy(d => d.CreatedAt).ToListAsync(ct);
        return Envelope.Ok(rows.Select(VerificationDocumentResponse.From).ToList());
    }

    private static async Task<EnvelopeResult<VerificationDocumentResponse>> AddDocument(
        VerificationDocumentRequest body, CurrentProvider provider, GridDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (body.DocumentType is not { } type)
            throw Errors.Validation([new FieldError("documentType", "Say which document this is.")]);

        var (consumer, account, _) = await provider.LoadEditableAsync(ct);
        var purposes = type is VerificationDocumentType.ProfessionalCertificate or VerificationDocumentType.BusinessRegistration
            ? CertificatePurposes
            : IdentityPurposes;
        var media = await MediaEndpoints.FindAttachableAsync(db, consumer.Id, body.StorageKey, purposes, ct);

        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (VerificationDocument.IsSingleInstance(type))
        {
            await db.VerificationDocuments
                .Where(d => d.ProviderAccountId == account.Id && d.DocumentType == type && d.SupersededAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.SupersededAt, now), ct);
        }

        media.AttachedAt = now;
        var document = new VerificationDocument
        {
            ProviderAccountId = account.Id,
            DocumentType = type,
            MediaObjectId = media.Id,
            StorageKey = media.StorageKey,
            FileName = media.FileName,
            ContentType = media.ContentType,
            SizeBytes = media.SizeBytes ?? 0,
        };
        db.VerificationDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Envelope.Created(VerificationDocumentResponse.From(document));
    }

    // ── Standards ──────────────────────────────────────────────────────────

    private static async Task<EnvelopeResult<StandardsResponse>> GetStandards(
        CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        return Envelope.Ok(await StandardsAsync(db, accountId, ct));
    }

    /// <summary>Records who accepted the current version and when. Accepting again changes nothing.</summary>
    private static async Task<EnvelopeResult<StandardsResponse>> AcceptStandards(
        CurrentProvider provider, GridDbContext db, TimeProvider time, CancellationToken ct)
    {
        var (consumer, account, _) = await provider.LoadAsync(ct);
        var existing = await StandardsAsync(db, account.Id, ct);
        if (existing.Accepted) return Envelope.Ok(existing);

        db.StandardsAcceptances.Add(new StandardsAcceptance
        {
            ProviderAccountId = account.Id,
            StandardsVersion = ProviderStandards.CurrentVersion,
            AcceptedByConsumerId = consumer.Id,
            AcceptedAt = time.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(OneAcceptancePerVersionIndex))
        {
            // Accepted concurrently; the first acceptance stands.
        }
        return Envelope.Ok(await StandardsAsync(db, account.Id, ct));
    }

    private static async Task<StandardsResponse> StandardsAsync(GridDbContext db, Guid accountId, CancellationToken ct)
    {
        var acceptedAt = await db.StandardsAcceptances
            .Where(s => s.ProviderAccountId == accountId && s.StandardsVersion == ProviderStandards.CurrentVersion)
            .Select(s => (DateTimeOffset?)s.AcceptedAt)
            .SingleOrDefaultAsync(ct);
        return new StandardsResponse(ProviderStandards.CurrentVersion, acceptedAt is not null, acceptedAt);
    }
}
