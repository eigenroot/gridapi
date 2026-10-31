using System.Text.RegularExpressions;

namespace Grid.Api.Common;

/// <summary>Collects field errors for a request and throws them together as one validation error.</summary>
public sealed partial class Validator
{
    private readonly List<FieldError> _errors = [];

    /// <summary>Trims and checks a required string. Returns the trimmed value.</summary>
    public string Required(string field, string? value, int maxLength, string label)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
            Add(field, $"{label} is required.");
        else if (trimmed.Length > maxLength)
            Add(field, $"{label} must be {maxLength} characters or fewer.");
        return trimmed;
    }

    /// <summary>Trims an optional string; blank becomes null.</summary>
    public string? Optional(string field, string? value, int maxLength, string label)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength)
            Add(field, $"{label} must be {maxLength} characters or fewer.");
        return trimmed;
    }

    public string? Email(string field, string? value)
    {
        var email = Optional(field, value, 254, "Email");
        if (email is null) return null;
        if (!EmailPattern().IsMatch(email))
            Add(field, "Enter a valid email address.");
        return email.ToLowerInvariant();
    }

    public void Add(string field, string message) => _errors.Add(new FieldError(field, message));

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0) throw Errors.Validation(_errors);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
