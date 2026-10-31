using System.Text.RegularExpressions;
using Grid.Api.Common;
using Grid.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Providers;

public sealed record PayoutMethodRequest(
    PayoutMethodType? MethodType,
    string? ProviderName,
    string? AccountName,
    string? AccountNumber,
    bool? MakeDefault);

/// <summary>Only the masked number is ever returned.</summary>
public sealed record PayoutMethodResponse(
    Guid Id,
    PayoutMethodType MethodType,
    string ProviderName,
    string AccountName,
    string MaskedAccountNumber,
    PayoutVerificationStatus VerificationStatus,
    bool IsDefault,
    DateTimeOffset CreatedAt)
{
    public static PayoutMethodResponse From(PayoutMethod m) =>
        new(m.Id, m.MethodType, m.ProviderName, m.AccountName, m.MaskedAccountNumber, m.VerificationStatus,
            m.IsDefault, m.CreatedAt);
}

public static partial class PayoutMethodEndpoints
{
    private const string OneDefaultIndex = "ix_payout_methods_one_default";

    public static void MapPayoutMethodEndpoints(this IEndpointRouteBuilder api)
    {
        var payouts = api.MapGroup("/payout-methods").WithTags("Provider onboarding").RequireAuthorization();

        payouts.MapGet("", List);
        payouts.MapPost("", Add);
    }

    private static async Task<EnvelopeResult<List<PayoutMethodResponse>>> List(
        CurrentProvider provider, GridDbContext db, CancellationToken ct)
    {
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var rows = await db.PayoutMethods.Where(m => m.ProviderAccountId == accountId && m.ArchivedAt == null)
            .OrderByDescending(m => m.IsDefault).ThenBy(m => m.CreatedAt).ToListAsync(ct);
        return Envelope.Ok(rows.Select(PayoutMethodResponse.From).ToList());
    }

    /// <summary>
    /// Saves a payout destination. The app resends the payout step whenever it is saved, so the
    /// same type and number updates the existing method instead of adding another. The first
    /// method becomes the default; <c>makeDefault</c> moves the default.
    /// </summary>
    private static async Task<EnvelopeResult<PayoutMethodResponse>> Add(
        PayoutMethodRequest body, CurrentProvider provider, GridDbContext db, PayoutProtector protector, CancellationToken ct)
    {
        var v = new Validator();
        if (body.MethodType is null) v.Add("methodType", "Choose mobile money or a bank account.");
        var providerName = v.Required("providerName", body.ProviderName, 100, "Provider");
        var accountName = v.Required("accountName", body.AccountName, 120, "Account name");
        var number = body.MethodType is { } type ? NormalizeNumber(v, type, body.AccountNumber) : null;
        v.ThrowIfInvalid();

        var methodType = body.MethodType!.Value;
        var accountId = (await provider.LoadAsync(ct)).Account.Id;
        var fingerprint = protector.Fingerprint(methodType, number!);

        var method = await db.PayoutMethods.SingleOrDefaultAsync(
            m => m.ProviderAccountId == accountId && m.AccountNumberFingerprint == fingerprint && m.ArchivedAt == null, ct);
        var created = method is null;
        var hasDefault = await db.PayoutMethods.AnyAsync(
            m => m.ProviderAccountId == accountId && m.IsDefault && m.ArchivedAt == null, ct);

        method ??= db.PayoutMethods.Add(new PayoutMethod
        {
            ProviderAccountId = accountId,
            MethodType = methodType,
            ProviderName = providerName,
            AccountName = accountName,
            AccountNumberEncrypted = protector.Encrypt(number!),
            AccountNumberFingerprint = fingerprint,
            MaskedAccountNumber = PayoutProtector.Mask(number!),
        }).Entity;
        method.ProviderName = providerName;
        method.AccountName = accountName;
        var makeDefault = body.MakeDefault == true || !hasDefault || method.IsDefault;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (makeDefault)
        {
            await db.PayoutMethods
                .Where(m => m.ProviderAccountId == accountId && m.IsDefault && m.Id != method.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDefault, false), ct);
        }
        method.IsDefault = makeDefault;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(OneDefaultIndex))
        {
            throw Errors.Conflict("payout_methods.conflict", "Your payout methods changed at the same time. Try again.");
        }
        await tx.CommitAsync(ct);

        var response = PayoutMethodResponse.From(method);
        return created ? Envelope.Created(response) : Envelope.Ok(response);
    }

    /// <summary>Mobile money numbers become <c>233…</c>; bank numbers lose spaces and dashes.</summary>
    private static string? NormalizeNumber(Validator v, PayoutMethodType type, string? input)
    {
        if (type == PayoutMethodType.MobileMoney)
        {
            if (PhoneNumbers.TryNormalize(input, out var phone)) return phone;
            v.Add("accountNumber", "Enter a valid mobile money number.");
            return null;
        }

        var bank = Separators().Replace(input ?? "", "").ToUpperInvariant();
        if (BankNumber().IsMatch(bank)) return bank;
        v.Add("accountNumber", "Enter a valid bank account number.");
        return null;
    }

    [GeneratedRegex(@"[\s-]")]
    private static partial Regex Separators();

    [GeneratedRegex("^[A-Z0-9]{6,34}$")]
    private static partial Regex BankNumber();
}
