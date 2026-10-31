using Grid.Api.Data;

namespace Grid.Api.Features.Media;

/// <summary>What an upload is for; decides what it may later be attached to.</summary>
public enum MediaPurpose { Portfolio, Certificates, Identity, ProfilePhoto }

public enum MediaStatus { Requested, Uploaded, Confirmed }

/// <summary>
/// One uploaded file. The storage key is unguessable, but it is a locator, not an access grant:
/// identity documents live here, so every read checks ownership.
/// </summary>
public sealed class MediaObject : Entity
{
    /// <summary>Flat (<c>{32 hex}.{ext}</c>), so it can sit in a URL path segment as is.</summary>
    public required string StorageKey { get; set; }
    public Guid OwnerConsumerId { get; set; }
    public MediaPurpose Purpose { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long DeclaredSizeBytes { get; set; }

    /// <summary>Bytes actually received; set once uploaded.</summary>
    public long? SizeBytes { get; set; }

    public MediaStatus Status { get; set; } = MediaStatus.Requested;
    public DateTimeOffset UploadExpiresAt { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>When it was attached to a document or portfolio item; each upload is attached once.</summary>
    public DateTimeOffset? AttachedAt { get; set; }
}
