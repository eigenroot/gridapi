using System.Security.Cryptography;
using System.Text;
using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Integrations.Sms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Auth;

public sealed record OtpRequestResult(
    string PhoneNumber,
    int ExpiresInSeconds,
    int ResendAfterSeconds,
    string? DevelopmentCode);

/// <summary>
/// Issues and checks login codes. GRID generates the code itself and uses SMS providers only to
/// deliver it, so switching providers never invalidates a code that is already out.
/// </summary>
public sealed class OtpService(
    GridDbContext db,
    SmsRouter sms,
    IOptions<OtpOptions> options,
    TimeProvider time)
{
    public const string DevelopmentCode = "123456";

    public async Task<OtpRequestResult> RequestAsync(string phoneNumber, AppKind app, string? ip, CancellationToken ct)
    {
        var config = options.Value;
        var now = time.GetUtcNow();

        await EnforceRequestLimitsAsync(phoneNumber, now, ct);

        var reviewCode = ReviewCodeFor(phoneNumber);
        var code = reviewCode ?? (config.DevelopmentMode ? DevelopmentCode : NewCode(config.Length));

        var challenge = new OtpChallenge
        {
            PhoneNumber = phoneNumber,
            CodeHash = "",
            ExpiresAt = now.AddSeconds(config.TtlSeconds),
            MaxAttempts = config.MaxAttempts,
            App = app,
            RequestIp = ip,
        };
        challenge.CodeHash = Hash(challenge.Id, code);

        // Send before saving: if delivery fails, nothing changes and the previous code (if any)
        // still works.
        if (reviewCode is null)
        {
            var minutes = Math.Max(1, config.TtlSeconds / 60);
            var result = await sms.SendAsync(new SmsMessage(
                phoneNumber,
                $"Your GRID code is {code}. It expires in {minutes} minutes. Never share it with anyone.",
                "Otp"), ct);
            if (!result.Accepted) throw Errors.SmsUnavailable();

            challenge.SmsProvider = result.Provider.ToString();
            challenge.SmsProviderMessageId = result.ProviderMessageId;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.OtpChallenges
            .Where(c => c.PhoneNumber == phoneNumber && c.ConsumedAt == null && c.SupersededAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.SupersededAt, now), ct);
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new OtpRequestResult(
            phoneNumber,
            config.TtlSeconds,
            config.ResendCooldownSeconds,
            config.DevelopmentMode ? code : null);
    }

    /// <summary>Consumes the phone's current code if <paramref name="code"/> matches it; throws otherwise.</summary>
    public async Task VerifyAsync(string phoneNumber, string code, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var challenge = await db.OtpChallenges
            .AsNoTracking()
            .Where(c => c.PhoneNumber == phoneNumber && c.ConsumedAt == null && c.SupersededAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (challenge is null || challenge.ExpiresAt <= now) throw Errors.OtpExpired();
        if (challenge.Attempts >= challenge.MaxAttempts) throw Errors.OtpAttemptsExceeded();

        var expected = Convert.FromHexString(challenge.CodeHash);
        var actual = Convert.FromHexString(Hash(challenge.Id, code.Trim()));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            // Counted in the database so parallel guesses can't exceed the limit.
            var counted = await db.OtpChallenges
                .Where(c => c.Id == challenge.Id && c.Attempts < c.MaxAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), ct);
            var remaining = challenge.MaxAttempts - challenge.Attempts - 1;
            if (counted == 0 || remaining <= 0) throw Errors.OtpAttemptsExceeded();
            throw Errors.OtpInvalid(remaining);
        }

        // Only one request can consume a code.
        var consumed = await db.OtpChallenges
            .Where(c => c.Id == challenge.Id && c.ConsumedAt == null && c.SupersededAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);
        if (consumed == 0) throw Errors.OtpExpired();
    }

    private async Task EnforceRequestLimitsAsync(string phoneNumber, DateTimeOffset now, CancellationToken ct)
    {
        var config = options.Value;
        var windowStart = now.AddHours(-1);
        var recent = await db.OtpChallenges
            .Where(c => c.PhoneNumber == phoneNumber && c.CreatedAt > windowStart)
            .Select(c => c.CreatedAt)
            .OrderByDescending(c => c)
            .ToListAsync(ct);

        if (recent.Count > 0)
        {
            var sinceLast = now - recent[0];
            var cooldown = TimeSpan.FromSeconds(config.ResendCooldownSeconds);
            if (sinceLast < cooldown)
                throw Errors.OtpRateLimited(SecondsUntil(cooldown - sinceLast));
        }

        if (recent.Count >= config.MaxRequestsPerPhonePerHour)
        {
            // The window frees up when the oldest counted request turns an hour old.
            var oldestCounted = recent[config.MaxRequestsPerPhonePerHour - 1];
            throw Errors.OtpRateLimited(SecondsUntil(oldestCounted.AddHours(1) - now));
        }
    }

    private string? ReviewCodeFor(string phoneNumber)
    {
        foreach (var (phone, code) in options.Value.ReviewNumbers)
        {
            if (PhoneNumbers.TryNormalize(phone, out var normalized) && normalized == phoneNumber)
                return code;
        }
        return null;
    }

    internal string Hash(Guid challengeId, string code)
    {
        var key = Encoding.UTF8.GetBytes(options.Value.Pepper);
        var data = Encoding.UTF8.GetBytes($"{challengeId:N}:{code}");
        return Convert.ToHexString(HMACSHA256.HashData(key, data));
    }

    private static string NewCode(int length)
    {
        var digits = new char[length];
        for (var i = 0; i < length; i++)
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return new string(digits);
    }

    private static int SecondsUntil(TimeSpan span) => Math.Max(1, (int)Math.Ceiling(span.TotalSeconds));
}
