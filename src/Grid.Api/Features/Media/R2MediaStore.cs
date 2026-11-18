using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Media;

/// <summary>
/// Files in a private Cloudflare R2 bucket, reached through its S3-compatible API.
///
/// Phones upload straight to R2 with a short-lived presigned link, so large photos never pass
/// through the API. The link signs the content type and the exact size, which means R2 itself
/// turns away a different file type or a bigger file. Reads still go through the API, which is
/// where ownership is checked; the bucket has no public access.
/// </summary>
public sealed class R2MediaStore(HttpClient http, IOptions<MediaOptions> options, TimeProvider time) : IMediaStore
{
    private static readonly TimeSpan ServerLink = TimeSpan.FromMinutes(5);

    private readonly R2Options _r2 = options.Value.R2;

    public UploadTarget? DirectUpload(string key, string contentType, long sizeBytes, DateTimeOffset expiresAt)
    {
        var now = time.GetUtcNow();
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = contentType,
            ["Content-Length"] = sizeBytes.ToString(),
        };
        var url = Presign(HttpMethod.Put, key, expiresAt - now, now, headers);
        return new UploadTarget(url, headers);
    }

    public Task<long?> WriteAsync(string key, Stream content, long maxBytes, CancellationToken ct) =>
        throw new NotSupportedException("R2 uploads go straight to the bucket.");

    public async Task<long?> SizeAsync(string key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, Presign(HttpMethod.Head, key));
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return response.Content.Headers.ContentLength;
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var response = await http.GetAsync(Presign(HttpMethod.Get, key), HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        using var response = await http.DeleteAsync(Presign(HttpMethod.Delete, key), ct);
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }

    private string Presign(HttpMethod method, string key) =>
        Presign(method, key, ServerLink, time.GetUtcNow(), null);

    private string Presign(
        HttpMethod method, string key, TimeSpan validFor, DateTimeOffset now, IReadOnlyDictionary<string, string>? signedHeaders)
    {
        if (!MediaKeys.IsValid(key)) throw new ArgumentException("Invalid storage key.", nameof(key));
        var endpoint = _r2.EndpointUri;
        // Path-style addressing: the bucket is the first path segment.
        var path = $"/{_r2.Bucket}/{_r2.KeyPrefix}{key}";
        return SigV4.PresignUrl(
            method.Method, endpoint.Scheme, endpoint.Authority, path, _r2.AccessKeyId, _r2.SecretAccessKey,
            region: "auto", now, validFor, signedHeaders);
    }
}

/// <summary>
/// AWS Signature Version 4 query-string signing for S3-compatible stores. Only what GRID needs:
/// a presigned URL for one object, optionally binding request headers into the signature.
/// </summary>
public static class SigV4
{
    private const string Algorithm = "AWS4-HMAC-SHA256";

    public static string PresignUrl(
        string method, string scheme, string host, string path, string accessKeyId, string secretAccessKey,
        string region, DateTimeOffset now, TimeSpan validFor, IReadOnlyDictionary<string, string>? signedHeaders = null)
    {
        var utc = now.UtcDateTime;
        var amzDate = utc.ToString("yyyyMMdd'T'HHmmss'Z'");
        var date = utc.ToString("yyyyMMdd");
        var scope = $"{date}/{region}/s3/aws4_request";
        var seconds = Math.Clamp((long)validFor.TotalSeconds, 1, 604800);

        // Header names are lower-cased and sorted; `host` is always signed.
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["host"] = host };
        foreach (var (name, value) in signedHeaders ?? new Dictionary<string, string>())
            headers[name.ToLowerInvariant()] = value.Trim();
        var signedNames = string.Join(';', headers.Keys);

        var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"] = Algorithm,
            ["X-Amz-Credential"] = $"{accessKeyId}/{scope}",
            ["X-Amz-Date"] = amzDate,
            ["X-Amz-Expires"] = seconds.ToString(),
            ["X-Amz-SignedHeaders"] = signedNames,
        };
        var canonicalQuery = string.Join('&', query.Select(p => $"{Encode(p.Key)}={Encode(p.Value)}"));
        var canonicalPath = string.Join('/', path.Split('/').Select(Encode));
        var canonicalHeaders = string.Concat(headers.Select(h => $"{h.Key}:{h.Value}\n"));

        var canonicalRequest = $"{method}\n{canonicalPath}\n{canonicalQuery}\n{canonicalHeaders}\n{signedNames}\nUNSIGNED-PAYLOAD";
        var stringToSign = $"{Algorithm}\n{amzDate}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";

        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), date);
        key = Hmac(key, region);
        key = Hmac(key, "s3");
        key = Hmac(key, "aws4_request");
        var signature = Hex(Hmac(key, stringToSign));

        return $"{scheme}://{host}{canonicalPath}?{canonicalQuery}&X-Amz-Signature={signature}";
    }

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    /// <summary>RFC 3986 encoding: everything but unreserved characters, upper-case hex.</summary>
    private static string Encode(string value)
    {
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.' or '~') builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2"));
        }
        return builder.ToString();
    }
}
