using System.Text;
using System.Text.RegularExpressions;

namespace Grid.Api.Features.Media;

/// <summary>The content types GRID accepts, and a check that the bytes really are that type.</summary>
public static class MediaTypes
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic",
        ["application/pdf"] = ".pdf",
        ["video/mp4"] = ".mp4",
    };

    private static readonly HashSet<string> HeifBrands = ["heic", "heix", "hevc", "hevx", "heim", "heis", "mif1", "msf1"];

    /// <summary>Bytes needed to recognise any accepted type.</summary>
    public const int SniffLength = 16;

    public static bool IsAllowed(string contentType) => Extensions.ContainsKey(contentType);

    public static string ExtensionFor(string contentType) => Extensions[contentType];

    /// <summary>The media type of a <c>Content-Type</c> header, without parameters, lower-cased.</summary>
    public static string Normalize(string? header) =>
        (header ?? "").Split(';')[0].Trim().ToLowerInvariant();

    /// <summary>True when the file's leading bytes match the declared type.</summary>
    public static bool Matches(string contentType, ReadOnlySpan<byte> head) => contentType switch
    {
        "image/jpeg" => head.StartsWith((byte[])[0xFF, 0xD8, 0xFF]),
        "image/png" => head.StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "image/webp" => head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8),
        "application/pdf" => head.StartsWith("%PDF-"u8),
        "image/heic" => Brand(head) is { } brand && HeifBrands.Contains(brand),
        "video/mp4" => Brand(head) is { } brand && !HeifBrands.Contains(brand),
        _ => false,
    };

    // ISO base media files (MP4, HEIC) open with a box: 4-byte size, "ftyp", then the brand.
    private static string? Brand(ReadOnlySpan<byte> head) =>
        head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8) ? Encoding.ASCII.GetString(head[8..12]) : null;
}

public static partial class MediaKeys
{
    public static string New(string contentType) => Guid.NewGuid().ToString("N") + MediaTypes.ExtensionFor(contentType);

    public static bool IsValid(string? key) => key is not null && KeyPattern().IsMatch(key);

    [GeneratedRegex(@"^[0-9a-f]{32}\.[a-z0-9]{3,4}$")]
    private static partial Regex KeyPattern();
}
