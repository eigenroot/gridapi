using System.Text;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Auth;

public sealed class OtpOptions
{
    public const string Section = "Otp";

    public int Length { get; set; } = 6;
    public int TtlSeconds { get; set; } = 300;
    public int MaxAttempts { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxRequestsPerPhonePerHour { get; set; } = 5;

    /// <summary>
    /// Every code is <c>123456</c> and is echoed back as <c>developmentCode</c>. The app refuses
    /// to start with this on in Production.
    /// </summary>
    public bool DevelopmentMode { get; set; }

    /// <summary>Server secret mixed into code hashes; at least 32 characters.</summary>
    public string Pepper { get; set; } = "";

    /// <summary>
    /// Fixed codes for app-store review accounts, keyed by phone in any accepted format.
    /// No SMS is sent to these numbers.
    /// </summary>
    public Dictionary<string, string> ReviewNumbers { get; set; } = [];
}

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "grid-api";
    public string Audience { get; set; } = "grid-apps";

    /// <summary>HMAC-SHA256 key; at least 32 bytes.</summary>
    public string SigningKey { get; set; } = "";

    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 60;
}

public sealed class OtpOptionsValidator : IValidateOptions<OtpOptions>
{
    public ValidateOptionsResult Validate(string? name, OtpOptions o)
    {
        var failures = new List<string>();
        if (o.Pepper.Length < 32) failures.Add("Otp:Pepper must be at least 32 characters.");
        if (o.Length is < 4 or > 10) failures.Add("Otp:Length must be between 4 and 10.");
        if (o.TtlSeconds < 60) failures.Add("Otp:TtlSeconds must be at least 60.");
        if (o.MaxAttempts < 1) failures.Add("Otp:MaxAttempts must be at least 1.");
        if (o.MaxRequestsPerPhonePerHour < 1) failures.Add("Otp:MaxRequestsPerPhonePerHour must be at least 1.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions o) =>
        Encoding.UTF8.GetByteCount(o.SigningKey) < 32
            ? ValidateOptionsResult.Fail("Jwt:SigningKey must be at least 32 bytes.")
            : ValidateOptionsResult.Success;
}
