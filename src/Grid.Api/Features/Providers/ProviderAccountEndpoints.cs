using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Consumers;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Providers;

public sealed record CreateProviderAccountRequest(ProviderAccountType? AccountType, string? DisplayName, string? Email);

/// <summary>Full replace. <c>accountType</c> is optional and left unchanged when omitted.</summary>
public sealed record UpdateProviderAccountRequest(
    string? DisplayName,
    string? LegalName,
    string? Description,
    string? Email,
    string? ProfileImageUrl,
    string? BusinessRegistrationNumber,
    string? City,
    string? PrimaryArea,
    int? YearsOfExperience,
    DeliveryMode? DeliveryMode,
    string? PremisesAddress,
    ProviderAccountType? AccountType = null);

/// <summary><c>profileImageUrl</c> is part of the app's model; profile photos arrive as documents, so it is always null.</summary>
public sealed record ProviderAccountResponse(
    Guid Id,
    ProviderAccountType AccountType,
    string DisplayName,
    string? LegalName,
    string? Description,
    string? Email,
    string? ProfileImageUrl,
    string? BusinessRegistrationNumber,
    string? City,
    string? PrimaryArea,
    int YearsOfExperience,
    DeliveryMode DeliveryMode,
    string? PremisesAddress,
    ProviderVerificationStatus VerificationStatus,
    OperatingStatus OperatingStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ProviderAccountResponse From(ProviderAccount a) =>
        new(a.Id, a.AccountType, a.DisplayName, a.LegalName, a.Description, a.Email, null,
            a.BusinessRegistrationNumber, a.City, a.PrimaryArea, a.YearsOfExperience, a.DeliveryMode,
            a.PremisesAddress, a.VerificationStatus, a.OperatingStatus, a.CreatedAt, a.UpdatedAt);
}

public static class ProviderAccountEndpoints
{
    private const string OneAccountPerConsumerIndex = "ix_provider_accounts_owner_consumer_id";

    public static void MapProviderAccountEndpoints(this IEndpointRouteBuilder api)
    {
        var accounts = api.MapGroup("/provider-accounts").WithTags("Provider onboarding").RequireAuthorization();

        accounts.MapPost("", Create);
        accounts.MapGet("/me", Get);
        accounts.MapPut("/me", Update);
    }

    /// <summary>
    /// Turns the signed-in person into a provider: creates the account and its draft application.
    /// There is no provider-specific sign-up; <c>user.providerAccountId</c> is set from here on.
    /// </summary>
    private static async Task<EnvelopeResult<ProviderAccountResponse>> Create(
        CreateProviderAccountRequest body, CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var v = new Validator();
        if (body.AccountType is null) v.Add("accountType", "Choose individual or business.");
        var displayName = v.Required("displayName", body.DisplayName, 120, "Name");
        var email = v.Email("email", body.Email);
        v.ThrowIfInvalid();

        var consumer = await current.LoadAsync(ct);
        if (await db.ProviderAccounts.AnyAsync(a => a.OwnerConsumerId == consumer.Id, ct)) throw AlreadyExists();

        var account = new ProviderAccount
        {
            OwnerConsumerId = consumer.Id,
            AccountType = body.AccountType!.Value,
            DisplayName = displayName,
            Email = email,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.ProviderAccounts.Add(account);
        db.ProviderApplications.Add(new ProviderApplication { ProviderAccountId = account.Id });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(OneAccountPerConsumerIndex))
        {
            throw AlreadyExists();
        }
        await tx.CommitAsync(ct);

        return Envelope.Created(ProviderAccountResponse.From(account));
    }

    private static async Task<EnvelopeResult<ProviderAccountResponse>> Get(CurrentProvider provider, CancellationToken ct) =>
        Envelope.Ok(ProviderAccountResponse.From((await provider.LoadAsync(ct)).Account));

    private static async Task<EnvelopeResult<ProviderAccountResponse>> Update(
        UpdateProviderAccountRequest body, CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var v = new Validator();
        var displayName = v.Required("displayName", body.DisplayName, 120, "Name");
        var legalName = v.Optional("legalName", body.LegalName, 200, "Legal name");
        var description = v.Optional("description", body.Description, 4000, "Description");
        var email = v.Email("email", body.Email);
        var registration = v.Optional("businessRegistrationNumber", body.BusinessRegistrationNumber, 50, "Registration number");
        var city = v.Optional("city", body.City, 100, "City");
        var primaryArea = v.Optional("primaryArea", body.PrimaryArea, 100, "Area");
        var premises = v.Optional("premisesAddress", body.PremisesAddress, 300, "Premises address");
        if (body.YearsOfExperience is not (>= 0 and <= 80))
            v.Add("yearsOfExperience", "Enter your years of experience.");
        v.ThrowIfInvalid();

        var deliveryMode = body.DeliveryMode ?? DeliveryMode.TravelsToCustomer;
        if (deliveryMode != DeliveryMode.TravelsToCustomer && premises is null)
        {
            const string message = "Add the address customers come to.";
            throw new ApiException(400, "providers.premises_address_required", message,
                fields: [new FieldError("premisesAddress", message)]);
        }

        var account = (await provider.LoadEditableAsync(ct)).Account;
        account.AccountType = body.AccountType ?? account.AccountType;
        account.DisplayName = displayName;
        account.LegalName = legalName;
        account.Description = description;
        account.Email = email;
        account.BusinessRegistrationNumber = registration;
        account.City = city;
        account.PrimaryArea = primaryArea;
        account.YearsOfExperience = body.YearsOfExperience!.Value;
        account.DeliveryMode = deliveryMode;
        account.PremisesAddress = premises;
        await db.SaveChangesAsync(ct);

        return Envelope.Ok(ProviderAccountResponse.From(account));
    }

    private static ApiException AlreadyExists() =>
        Errors.Conflict("providers.account_exists", "You already have a provider account.");
}
