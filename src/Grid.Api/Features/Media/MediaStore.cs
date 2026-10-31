using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Media;

public sealed class MediaOptions
{
    public const string Section = "Media";

    /// <summary>Where bytes are kept. Relative paths resolve against the content root.</summary>
    public string RootPath { get; set; } = "App_Data/media";

    /// <summary>The most a single upload may be, enforced on the bytes actually streamed.</summary>
    public long MaxBytes { get; set; } = 25 * 1024 * 1024;

    public int TicketMinutes { get; set; } = 15;

    /// <summary>
    /// Origin used to build upload URLs (e.g. <c>https://api.grid.africa</c>). When empty the
    /// request's own scheme and host are used, which is right behind Caddy with forwarded headers.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}

public sealed class MediaOptionsValidator : IValidateOptions<MediaOptions>
{
    public ValidateOptionsResult Validate(string? name, MediaOptions o)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(o.RootPath)) failures.Add("Media:RootPath is required.");
        if (o.MaxBytes < 1) failures.Add("Media:MaxBytes must be positive.");
        if (o.TicketMinutes < 1) failures.Add("Media:TicketMinutes must be at least 1.");
        if (!string.IsNullOrWhiteSpace(o.PublicBaseUrl) && !Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out _))
            failures.Add("Media:PublicBaseUrl must be an absolute URL.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Where upload bytes live. Callers have already checked ownership in the database.</summary>
public interface IMediaStore
{
    /// <summary>Writes the stream under <paramref name="key"/>; returns null (and keeps nothing) past <paramref name="maxBytes"/>.</summary>
    Task<long?> WriteAsync(string key, Stream content, long maxBytes, CancellationToken ct);

    Stream? OpenRead(string key);
}

/// <summary>Files on a host directory (a Docker volume in production).</summary>
public sealed class LocalMediaStore(IOptions<MediaOptions> options, IWebHostEnvironment env) : IMediaStore
{
    private readonly string _root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.RootPath));

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

    public Stream? OpenRead(string key)
    {
        var path = PathFor(key);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    // Keys are server-generated hex plus an extension, so they can't climb out of the root; the
    // first two characters fan files out across subdirectories.
    private string PathFor(string key)
    {
        if (!MediaKeys.IsValid(key)) throw new ArgumentException("Invalid storage key.", nameof(key));
        return Path.Combine(_root, key[..2], key);
    }
}
