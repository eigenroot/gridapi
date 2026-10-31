using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Consumers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Media;

public sealed record UploadTicketRequest(MediaPurpose? Purpose, string? FileName, string? ContentType, long? SizeBytes);

public sealed record UploadTicketResponse(
    string StorageKey,
    string UploadUrl,
    Dictionary<string, string> Headers,
    DateTimeOffset ExpiresAt);

public sealed record ConfirmUploadRequest(string? StorageKey);

public sealed record MediaResponse(
    string StorageKey,
    MediaPurpose Purpose,
    string FileName,
    string ContentType,
    long? SizeBytes,
    MediaStatus Status)
{
    public static MediaResponse From(MediaObject m) =>
        new(m.StorageKey, m.Purpose, m.FileName, m.ContentType, m.SizeBytes, m.Status);
}

/// <summary>
/// Three steps: ask for a ticket, PUT the bytes to its URL, confirm. Confirmation is where the
/// bytes are checked against what was declared; only confirmed uploads can be attached or served.
/// </summary>
public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this IEndpointRouteBuilder api)
    {
        var media = api.MapGroup("/media").WithTags("Media").RequireAuthorization();

        media.MapPost("/upload-requests", RequestUpload);
        media.MapPut("/uploads/{storageKey}", Upload);
        media.MapPost("/uploads/confirm", Confirm);
        media.MapGet("/{storageKey}", Download);
    }

    private static async Task<EnvelopeResult<UploadTicketResponse>> RequestUpload(
        UploadTicketRequest body,
        HttpRequest request,
        CurrentConsumer current,
        GridDbContext db,
        IOptions<MediaOptions> options,
        TimeProvider time,
        CancellationToken ct)
    {
        var o = options.Value;
        var v = new Validator();
        if (body.Purpose is null) v.Add("purpose", "Say what the file is for.");
        var fileName = v.Required("fileName", body.FileName, 200, "File name");
        if (body.SizeBytes is not > 0) v.Add("sizeBytes", "Give the file size in bytes.");
        v.ThrowIfInvalid();

        var contentType = MediaTypes.Normalize(body.ContentType);
        if (!MediaTypes.IsAllowed(contentType)) throw UnsupportedType();
        if (body.SizeBytes > o.MaxBytes) throw TooLarge(o.MaxBytes);

        var consumer = await current.LoadAsync(ct);
        var media = new MediaObject
        {
            StorageKey = MediaKeys.New(contentType),
            OwnerConsumerId = consumer.Id,
            Purpose = body.Purpose!.Value,
            FileName = fileName,
            ContentType = contentType,
            DeclaredSizeBytes = body.SizeBytes!.Value,
            UploadExpiresAt = time.GetUtcNow().AddMinutes(o.TicketMinutes),
        };
        db.MediaObjects.Add(media);
        await db.SaveChangesAsync(ct);

        // /api/v1 is the API's group prefix. The client PUTs to this absolute URL with its bearer token.
        var origin = string.IsNullOrWhiteSpace(o.PublicBaseUrl)
            ? $"{request.Scheme}://{request.Host}{request.PathBase}"
            : o.PublicBaseUrl.TrimEnd('/');
        return Envelope.Ok(new UploadTicketResponse(
            media.StorageKey,
            $"{origin}/api/v1/media/uploads/{media.StorageKey}",
            new Dictionary<string, string> { ["Content-Type"] = contentType },
            media.UploadExpiresAt));
    }

    /// <summary>Raw bytes. Can be repeated until the upload is confirmed.</summary>
    private static async Task<EnvelopeResult<MediaResponse>> Upload(
        string storageKey,
        HttpRequest request,
        CurrentConsumer current,
        GridDbContext db,
        IMediaStore store,
        IOptions<MediaOptions> options,
        TimeProvider time,
        CancellationToken ct)
    {
        var consumerId = (await current.LoadAsync(ct)).Id;
        var media = await FindOwnedAsync(db, consumerId, storageKey, ct);

        if (media.Status == MediaStatus.Confirmed)
            throw Errors.Conflict("media.already_confirmed", "This upload is already complete.");
        if (media.UploadExpiresAt <= time.GetUtcNow())
            throw new ApiException(400, "media.upload_expired", "This upload link has expired. Start the upload again.");
        if (MediaTypes.Normalize(request.ContentType) != media.ContentType)
            throw UnsupportedType();

        var limit = Math.Min(media.DeclaredSizeBytes, options.Value.MaxBytes);
        var written = await store.WriteAsync(media.StorageKey, request.Body, limit, ct)
                      ?? throw TooLarge(limit);

        media.SizeBytes = written;
        media.Status = MediaStatus.Uploaded;
        media.UploadedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Envelope.Ok(MediaResponse.From(media));
    }

    private static async Task<EnvelopeResult<MediaResponse>> Confirm(
        ConfirmUploadRequest body,
        CurrentConsumer current,
        GridDbContext db,
        IMediaStore store,
        TimeProvider time,
        CancellationToken ct)
    {
        var consumerId = (await current.LoadAsync(ct)).Id;
        var media = await FindOwnedAsync(db, consumerId, body.StorageKey, ct);
        if (media.Status == MediaStatus.Confirmed) return Envelope.Ok(MediaResponse.From(media));

        await using var stream = media.Status == MediaStatus.Uploaded ? store.OpenRead(media.StorageKey) : null;
        if (stream is null) throw NotUploaded();

        var head = new byte[MediaTypes.SniffLength];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        if (!MediaTypes.Matches(media.ContentType, head.AsSpan(0, read)))
            throw new ApiException(400, "media.unsupported_type", "The file isn't the type it says it is.");

        media.Status = MediaStatus.Confirmed;
        media.ConfirmedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Envelope.Ok(MediaResponse.From(media));
    }

    /// <summary>Owner-only for now; Ops reviewers get access when the review console exists.</summary>
    private static async Task<IResult> Download(
        string storageKey, CurrentConsumer current, GridDbContext db, IMediaStore store, CancellationToken ct)
    {
        var consumerId = (await current.LoadAsync(ct)).Id;
        var media = await FindOwnedAsync(db, consumerId, storageKey, ct);
        var stream = media.Status == MediaStatus.Confirmed ? store.OpenRead(media.StorageKey) : null;
        if (stream is null) throw NotFound();
        return Results.Stream(stream, media.ContentType);
    }

    /// <summary>
    /// A confirmed, unattached upload of one of <paramref name="purposes"/> belonging to the
    /// caller, ready to be attached to a document or portfolio item.
    /// </summary>
    internal static async Task<MediaObject> FindAttachableAsync(
        GridDbContext db, Guid consumerId, string? storageKey, IReadOnlyCollection<MediaPurpose> purposes, CancellationToken ct)
    {
        var media = await FindOwnedAsync(db, consumerId, storageKey, ct);
        if (media.Status != MediaStatus.Confirmed) throw NotUploaded();
        if (!purposes.Contains(media.Purpose))
            throw new ApiException(400, "media.wrong_purpose", "This file was uploaded for something else.",
                fields: [new FieldError("storageKey", "This file was uploaded for something else.")]);
        if (media.AttachedAt is not null)
            throw Errors.Conflict("media.already_attached", "This file is already attached.");
        return media;
    }

    private static async Task<MediaObject> FindOwnedAsync(GridDbContext db, Guid consumerId, string? storageKey, CancellationToken ct)
    {
        if (!MediaKeys.IsValid(storageKey)) throw NotFound();
        return await db.MediaObjects.SingleOrDefaultAsync(m => m.StorageKey == storageKey && m.OwnerConsumerId == consumerId, ct)
               ?? throw NotFound();
    }

    private static ApiException NotFound() => Errors.NotFound("media.not_found", "That file was not found.");

    private static ApiException NotUploaded() =>
        new(400, "media.not_uploaded", "The file hasn't finished uploading.");

    private static ApiException UnsupportedType() =>
        new(400, "media.unsupported_type", "Upload a JPEG, PNG, WebP or HEIC photo, a PDF or an MP4 video.");

    private static ApiException TooLarge(long maxBytes) =>
        new(400, "media.too_large", "This file is too large.", new { maxBytes });
}
