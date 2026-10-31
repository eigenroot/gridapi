using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Consumers;
using Grid.Api.Features.Providers;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Auth;

public sealed record OtpRequest(string? PhoneNumber);

public sealed record OtpVerifyRequest(string? PhoneNumber, string? Code, string? FullName);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record SessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    bool IsNewUser,
    UserResponse User);

public static class AuthEndpoints
{
    public const string OtpRateLimitPolicy = "otp";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/otp/request", RequestOtp).RequireRateLimiting(OtpRateLimitPolicy);
        auth.MapPost("/otp/verify", VerifyOtp).RequireRateLimiting(OtpRateLimitPolicy);
        auth.MapPost("/tokens/refresh", Refresh);
        auth.MapPost("/logout", Logout);
        auth.MapGet("/me", Me).RequireAuthorization();
    }

    private static async Task<EnvelopeResult<OtpRequestResult>> RequestOtp(
        OtpRequest body, HttpContext http, OtpService otp, CancellationToken ct)
    {
        var phone = PhoneNumbers.Normalize(body.PhoneNumber);
        var result = await otp.RequestAsync(phone, AppKindHeader.From(http), ClientIp(http), ct);
        return Envelope.Ok(result);
    }

    private static async Task<EnvelopeResult<SessionResponse>> VerifyOtp(
        OtpVerifyRequest body,
        HttpContext http,
        OtpService otp,
        TokenService tokens,
        GridDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var phone = PhoneNumbers.Normalize(body.PhoneNumber);
        if (string.IsNullOrWhiteSpace(body.Code))
            throw Errors.Validation([new FieldError("code", "Enter the code we sent you.")]);

        await otp.VerifyAsync(phone, body.Code, ct);

        var client = Client(http);
        var now = time.GetUtcNow();
        var consumer = await db.Consumers.SingleOrDefaultAsync(c => c.PhoneNumber == phone, ct);
        var isNew = consumer is null;

        if (consumer is null)
        {
            consumer = new Consumer
            {
                PhoneNumber = phone,
                PhoneNetwork = PhoneNumbers.GuessNetwork(phone),
                FirstSeenApp = client.App,
            };
            // GRID Pro sends the name at sign-in; GRID Home collects it on the next screen instead.
            if (SplitName(body.FullName) is { } name)
            {
                consumer.FirstName = name.First;
                consumer.LastName = name.Last;
                consumer.ProfileCompletedAt = now;
            }
            db.Consumers.Add(consumer);
        }
        else if (consumer.Status == ConsumerStatus.Suspended)
        {
            throw Errors.ConsumerSuspended();
        }

        consumer.LastLoginApp = client.App;
        consumer.LastLoginAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (isNew && ex.IsUniqueViolation())
        {
            // The same phone finished sign-in twice at once; use the row the other request made.
            db.ChangeTracker.Clear();
            consumer = await db.Consumers.SingleAsync(c => c.PhoneNumber == phone, ct);
            isNew = false;
        }

        var issued = await tokens.IssueAsync(consumer, client, ct);
        return Envelope.Ok(await ToSessionAsync(db, issued, consumer, isNew, ct));
    }

    private static async Task<EnvelopeResult<SessionResponse>> Refresh(
        RefreshRequest body, HttpContext http, TokenService tokens, GridDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.RefreshToken)) throw Errors.RefreshInvalid();
        var (issued, consumer) = await tokens.RefreshAsync(body.RefreshToken, Client(http), ct);
        return Envelope.Ok(await ToSessionAsync(db, issued, consumer, isNew: false, ct));
    }

    private static async Task<EnvelopeResult<object>> Logout(
        RefreshRequest body, TokenService tokens, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(body.RefreshToken)) await tokens.RevokeAsync(body.RefreshToken, ct);
        return Envelope.Empty();
    }

    private static async Task<EnvelopeResult<UserResponse>> Me(CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var consumer = await current.LoadAsync(ct);
        return Envelope.Ok(UserResponse.From(consumer, await CurrentProvider.AccountIdForAsync(db, consumer.Id, ct)));
    }

    private static async Task<SessionResponse> ToSessionAsync(
        GridDbContext db, IssuedTokens t, Consumer consumer, bool isNew, CancellationToken ct) =>
        new(t.AccessToken, t.AccessTokenExpiresAt, t.RefreshToken, t.RefreshTokenExpiresAt, isNew,
            UserResponse.From(consumer, await CurrentProvider.AccountIdForAsync(db, consumer.Id, ct)));

    internal static (string First, string? Last)? SplitName(string? fullName)
    {
        var parts = (fullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        var first = Clip(parts[0]);
        var last = parts.Length > 1 ? Clip(string.Join(' ', parts[1..])) : null;
        return (first, last);

        static string Clip(string s) => s.Length <= 100 ? s : s[..100];
    }

    private static ClientInfo Client(HttpContext http) =>
        new(AppKindHeader.From(http), ClientIp(http), http.Request.Headers.UserAgent.ToString());

    private static string? ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
