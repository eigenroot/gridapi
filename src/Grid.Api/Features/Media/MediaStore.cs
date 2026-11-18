using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Media;

public enum MediaProvider { Local, R2 }

public sealed class MediaOptions
{
    public const string Section = "Media";

    /// <summary><c>R2</c> keeps files in Cloudflare R2; <c>Local</c> keeps them on disk, for development and tests.</summary>
    public MediaProvider Provider { get; set; } = MediaProvider.Local;

    /// <summary>Local only: where bytes are kept. Relative paths resolve against the content root.</summary>
    public string RootPath { get; set; } = "App_Data/media";

    /// <summary>The most a single upload may be.</summary>
    public long MaxBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>How long an upload link stays valid.</summary>
    public int TicketMinutes { get; set; } = 15;

    /// <summary>
    /// Local only: origin used to build upload URLs (e.g. <c>https://api.grid.africa</c>). When
    /// empty the request's own scheme and host are used, which is right behind Caddy with
    /// forwarded headers.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    public R2Options R2 { get; set; } = new();
}

public sealed class R2Options
{
    /// <summary>The Cloudflare account id; it is the first label of the bucket's S3 endpoint.</summary>
    public string AccountId { get; set; } = "";
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public string Bucket { get; set; } = "";

    /// <summary>
    /// Overrides the endpoint worked out from the account id. Needed for a bucket in a
    /// jurisdiction, e.g. <c>https://{account}.eu.r2.cloudflarestorage.com</c>.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>Folder inside the bucket, so the bucket can hold other things too.</summary>
    public string KeyPrefix { get; set; } = "uploads/";

    public Uri EndpointUri => new(string.IsNullOrWhiteSpace(Endpoint)
        ? $"https://{AccountId}.r2.cloudflarestorage.com"
        : Endpoint.TrimEnd('/'));
}

public sealed class MediaOptionsValidator : IValidateOptions<MediaOptions>
{
    public ValidateOptionsResult Validate(string? name, MediaOptions o)
    {
        var failures = new List<string>();
        if (o.MaxBytes < 1) failures.Add("Media:MaxBytes must be positive.");
        if (o.TicketMinutes < 1) failures.Add("Media:TicketMinutes must be at least 1.");
        if (!string.IsNullOrWhiteSpace(o.PublicBaseUrl) && !Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out _))
            failures.Add("Media:PublicBaseUrl must be an absolute URL.");

        if (o.Provider == MediaProvider.Local)
        {
            if (string.IsNullOrWhiteSpace(o.RootPath)) failures.Add("Media:RootPath is required.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(o.R2.AccountId) && string.IsNullOrWhiteSpace(o.R2.Endpoint))
                failures.Add("Media:R2:AccountId (or Media:R2:Endpoint) is required when Media:Provider is R2.");
            if (string.IsNullOrWhiteSpace(o.R2.AccessKeyId)) failures.Add("Media:R2:AccessKeyId is required when Media:Provider is R2.");
            if (string.IsNullOrWhiteSpace(o.R2.SecretAccessKey)) failures.Add("Media:R2:SecretAccessKey is required when Media:Provider is R2.");
            if (string.IsNullOrWhiteSpace(o.R2.Bucket)) failures.Add("Media:R2:Bucket is required when Media:Provider is R2.");
            if (!string.IsNullOrWhiteSpace(o.R2.Endpoint) && !Uri.TryCreate(o.R2.Endpoint, UriKind.Absolute, out _))
                failures.Add("Media:R2:Endpoint must be an absolute URL.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Where a client sends an upload's bytes, and the headers it must send with them.</summary>
public sealed record UploadTarget(string Url, Dictionary<string, string> Headers);

/// <summary>Where upload bytes live. Callers have already checked ownership in the database.</summary>
public interface IMediaStore
{
    /// <summary>
    /// A link the client uploads straight to, when the store takes uploads itself. Null when the
    /// bytes come through the API instead (<see cref="WriteAsync"/>).
    /// </summary>
    UploadTarget? DirectUpload(string key, string contentType, long sizeBytes, DateTimeOffset expiresAt);

    /// <summary>Writes the stream under <paramref name="key"/>; returns null (and keeps nothing) past <paramref name="maxBytes"/>.</summary>
    Task<long?> WriteAsync(string key, Stream content, long maxBytes, CancellationToken ct);

    /// <summary>How many bytes are stored under the key, or null when nothing is.</summary>
    Task<long?> SizeAsync(string key, CancellationToken ct);

    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>Files on a host directory. Used in development and tests.</summary>
public sealed class LocalMediaStore(IOptions<MediaOptions> options, IWebHostEnvironment env) : IMediaStore
{
    private readonly string _root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.RootPath));

    public UploadTarget? DirectUpload(string key, string contentType, long sizeBytes, DateTimeOffset expiresAt) => null;

    public async Task<long?> WriteAsync(string key, Stream content, long maxBytes, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".partial";

        long total = 0;
        var buffer = new byte[81920];
        await using (var file = File.Create(temp))
        {
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > maxBytes) break;
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }

        if (total > maxBytes)
        {
            File.Delete(temp);
            return null;
        }
        File.Move(temp, path, overwrite: true);
        return total;
    }

    public Task<long?> SizeAsync(string key, CancellationToken ct)
    {
        var file = new FileInfo(PathFor(key));
        return Task.FromResult(file.Exists ? file.Length : (long?)null);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        File.Delete(PathFor(key));
        return Task.CompletedTask;
    }

    // Keys are server-generated hex plus an extension, so they can't climb out of the root; the
    // first two characters fan files out across subdirectories.
    private string PathFor(string key)
    {
        if (!MediaKeys.IsValid(key)) throw new ArgumentException("Invalid storage key.", nameof(key));
        return Path.Combine(_root, key[..2], key);
    }
}
