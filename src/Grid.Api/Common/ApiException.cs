namespace Grid.Api.Common;

/// <summary>
/// A failure the client is expected to handle. <see cref="Code"/> is stable and is what clients
/// branch on; the message is for people and may change.
/// </summary>
public sealed class ApiException(
    int statusCode,
    string code,
    string message,
    object? detail = null,
    IReadOnlyList<FieldError>? fields = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public object? Detail { get; } = detail;
    public IReadOnlyList<FieldError> Fields { get; } = fields ?? [];
}

public static class Errors
{
    public static ApiException Validation(IReadOnlyList<FieldError> fields) =>
        new(400, "validation.failed", "Some fields need attention.", fields: fields);

    public static ApiException InvalidPhone(string field = "phoneNumber") =>
        new(400, "auth.invalid_phone", "Enter a valid Ghana mobile number.",
            fields: [new FieldError(field, "Enter a valid Ghana mobile number.")]);

    public static ApiException OtpInvalid(int attemptsRemaining) =>
        new(400, "auth.otp_invalid", "That code is not correct.", new { attemptsRemaining });

    public static ApiException OtpExpired() =>
        new(400, "auth.otp_expired", "That code has expired. Request a new one.");

    public static ApiException OtpAttemptsExceeded() =>
        new(429, "auth.otp_attempts_exceeded", "Too many wrong codes. Request a new one.");

    public static ApiException OtpRateLimited(int retryAfterSeconds) =>
        new(429, "auth.otp_rate_limited", "Please wait before requesting another code.",
            new { retryAfterSeconds });

    public static ApiException RefreshInvalid() =>
        new(401, "auth.refresh_invalid", "Your session has ended. Sign in again.");

    public static ApiException Unauthenticated() =>
        new(401, "auth.unauthenticated", "Sign in to continue.");

    public static ApiException SmsUnavailable() =>
        new(503, "sms.unavailable", "We couldn't send your code right now. Try again shortly.");

    public static ApiException ConsumerSuspended() =>
        new(403, "consumers.suspended", "This account is suspended. Contact GRID support.");

    public static ApiException ProviderAccountNotFound() =>
        new(404, "providers.account_not_found", "You haven't started a provider account yet.");

    public static ApiException ProviderLocked() =>
        new(409, "providers.locked", "Your application is with GRID, so these details can't change right now.");

    public static ApiException NotFound(string code, string message) => new(404, code, message);

    public static ApiException Conflict(string code, string message) => new(409, code, message);
}
