using Grid.Api.Common;

namespace Grid.Api.Features.Consumers;

/// <summary>
/// The <c>user</c> object in auth responses. Keeps the shape GRID Pro already reads
/// (<c>id</c>, <c>fullName</c>, <c>phoneNumber</c>, <c>providerAccountId</c>).
/// </summary>
public sealed record UserResponse(
    Guid Id,
    string PhoneNumber,
    string FullName,
    string? FirstName,
    string? LastName,
    string? Email,
    bool ProfileComplete,
    Guid? ProviderAccountId)
{
    /// <summary><paramref name="providerAccountId"/> is set once the person has started a provider account.</summary>
    public static UserResponse From(Consumer c, Guid? providerAccountId) =>
        new(c.Id, c.PhoneNumber, c.FullName, c.FirstName, c.LastName, c.Email, c.ProfileComplete, providerAccountId);
}

public sealed record ConsumerResponse(
    Guid Id,
    string PhoneNumber,
    PhoneNetwork PhoneNetwork,
    string FullName,
    string? FirstName,
    string? LastName,
    string? Email,
    bool ProfileComplete,
    Guid? DefaultPropertyId,
    DateTimeOffset CreatedAt)
{
    public static ConsumerResponse From(Consumer c) =>
        new(c.Id, c.PhoneNumber, c.PhoneNetwork, c.FullName, c.FirstName, c.LastName, c.Email,
            c.ProfileComplete, c.DefaultPropertyId, c.CreatedAt);
}

public sealed record UpdateProfileRequest(string? FirstName, string? LastName, string? Email);

public sealed record SetDefaultPropertyRequest(Guid? PropertyId);
