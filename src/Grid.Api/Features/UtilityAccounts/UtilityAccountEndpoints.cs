using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Consumers;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.UtilityAccounts;

public sealed record UtilityAccountRequest(
    UtilityType? Type,
    string? AccountNumber,
    string? Nickname,
    Guid? PropertyId,
    bool? IsDefault);

/// <summary>
/// <c>lastPaidAt</c>, <c>lastAmount</c> and <c>dueDate</c> are part of the app's model but come
/// from payments, which don't exist yet; they are always null for now.
/// </summary>
public sealed record UtilityAccountResponse(
    Guid Id,
    UtilityType Type,
    string AccountNumber,
    string Nickname,
    Guid? PropertyId,
    bool IsDefault,
    DateTimeOffset? LastPaidAt,
    decimal? LastAmount,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static UtilityAccountResponse From(UtilityAccount a) =>
        new(a.Id, a.Type, a.AccountNumber, a.Nickname, a.PropertyId, a.IsDefault,
            null, null, null, a.CreatedAt, a.UpdatedAt);
}

public static class UtilityAccountEndpoints
{
    private const string UniqueNumberIndex = "ix_utility_accounts_owner_consumer_id_type_account_number";

    public static void MapUtilityAccountEndpoints(this IEndpointRouteBuilder api)
    {
        var accounts = api.MapGroup("/utility-accounts").WithTags("Saved accounts").RequireAuthorization();

        accounts.MapGet("", List);
        accounts.MapPost("", Create);
        accounts.MapGet("/{id:guid}", Get);
        accounts.MapPut("/{id:guid}", Update);
        accounts.MapDelete("/{id:guid}", Archive);
    }

    private static async Task<EnvelopeResult<List<UtilityAccountResponse>>> List(
        UtilityType? type, CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var consumerId = (await current.LoadAsync(ct)).Id;
        var query = db.UtilityAccounts.Where(a => a.OwnerConsumerId == consumerId && a.ArchivedAt == null);
        if (type is { } t) query = query.Where(a => a.Type == t);

        var rows = await query.OrderBy(a => a.Type).ThenBy(a => a.CreatedAt).ToListAsync(ct);
        return Envelope.Ok(rows.Select(UtilityAccountResponse.From).ToList());
    }

    private static async Task<EnvelopeResult<UtilityAccountResponse>> Get(
        Guid id, CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var consumerId = (await current.LoadAsync(ct)).Id;
        return Envelope.Ok(UtilityAccountResponse.From(await FindOwnedAsync(db, consumerId, id, ct)));
    }

    /// <summary>Saves a payee. The first account of a utility type becomes its default.</summary>
    private static async Task<EnvelopeResult<UtilityAccountResponse>> Create(
        UtilityAccountRequest body, CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var consumerId = (await current.LoadAsync(ct)).Id;
        var input = await ValidateAsync(db, consumerId, body, ct);

        await EnsureNotDuplicateAsync(db, consumerId, input.Type, input.AccountNumber, exceptId: null, ct);

        var hasDefault = await db.UtilityAccounts.AnyAsync(
            a => a.OwnerConsumerId == consumerId && a.Type == input.Type && a.IsDefault && a.ArchivedAt == null, ct);
        var makeDefault = input.IsDefault == true || !hasDefault;

        var account = new UtilityAccount
        {
            OwnerConsumerId = consumerId,
            Type = input.Type,
            AccountNumber = input.AccountNumber,
            Nickname = input.Nickname,
            PropertyId = input.PropertyId,
            IsDefault = makeDefault,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (makeDefault) await ClearDefaultAsync(db, consumerId, input.Type, exceptId: null, ct);
        db.UtilityAccounts.Add(account);
        await SaveAsync(db, ct);
        await tx.CommitAsync(ct);

        return Envelope.Created(UtilityAccountResponse.From(account));
    }

    /// <summary>Full replace. Sending <c>isDefault: true</c> moves the default to this account.</summary>
    private static async Task<EnvelopeResult<UtilityAccountResponse>> Update(
        Guid id, UtilityAccountRequest body, CurrentConsumer current, GridDbContext db, CancellationToken ct)
    {
        var consumerId = (await current.LoadAsync(ct)).Id;
        var account = await FindOwnedAsync(db, consumerId, id, ct);
        var input = await ValidateAsync(db, consumerId, body, ct);

        await EnsureNotDuplicateAsync(db, consumerId, input.Type, input.AccountNumber, exceptId: account.Id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var makeDefault = input.IsDefault ?? account.IsDefault;
        if (makeDefault) await ClearDefaultAsync(db, consumerId, input.Type, exceptId: account.Id, ct);

        account.Type = input.Type;
        account.AccountNumber = input.AccountNumber;
        account.Nickname = input.Nickname;
        account.PropertyId = input.PropertyId;
        account.IsDefault = makeDefault;
        await SaveAsync(db, ct);
        await tx.CommitAsync(ct);

        return Envelope.Ok(UtilityAccountResponse.From(account));
    }

    /// <summary>Archives the account; if it was the default, the newest remaining one of that type takes over.</summary>
    private static async Task<EnvelopeResult<object>> Archive(
        Guid id, CurrentConsumer current, GridDbContext db, TimeProvider time, CancellationToken ct)
    {
        var consumerId = (await current.LoadAsync(ct)).Id;
        var account = await FindOwnedAsync(db, consumerId, id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var wasDefault = account.IsDefault;
        account.ArchivedAt = time.GetUtcNow();
        account.IsDefault = false;
        await db.SaveChangesAsync(ct);

        if (wasDefault)
        {
            var next = await db.UtilityAccounts
                .Where(a => a.OwnerConsumerId == consumerId && a.Type == account.Type && a.ArchivedAt == null)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (next is not null)
            {
                next.IsDefault = true;
                await db.SaveChangesAsync(ct);
            }
        }

        await tx.CommitAsync(ct);
        return Envelope.Empty();
    }

    private static async Task<UtilityAccount> FindOwnedAsync(GridDbContext db, Guid consumerId, Guid id, CancellationToken ct) =>
        await db.UtilityAccounts.SingleOrDefaultAsync(
            a => a.Id == id && a.OwnerConsumerId == consumerId && a.ArchivedAt == null, ct)
        ?? throw Errors.NotFound("utility_accounts.not_found", "That saved account was not found.");

    private static Task ClearDefaultAsync(GridDbContext db, Guid consumerId, UtilityType type, Guid? exceptId, CancellationToken ct) =>
        db.UtilityAccounts
            .Where(a => a.OwnerConsumerId == consumerId && a.Type == type && a.IsDefault && a.ArchivedAt == null
                        && (exceptId == null || a.Id != exceptId))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);

    private static async Task EnsureNotDuplicateAsync(
        GridDbContext db, Guid consumerId, UtilityType type, string accountNumber, Guid? exceptId, CancellationToken ct)
    {
        var exists = await db.UtilityAccounts.AnyAsync(
            a => a.OwnerConsumerId == consumerId && a.Type == type && a.AccountNumber == accountNumber
                 && a.ArchivedAt == null && (exceptId == null || a.Id != exceptId), ct);
        if (exists) throw Duplicate();
    }

    private static async Task SaveAsync(GridDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(UniqueNumberIndex))
        {
            throw Duplicate();
        }
    }

    private static ApiException Duplicate() =>
        Errors.Conflict("utility_accounts.duplicate", "You've already saved this account.");

    private sealed record ValidInput(UtilityType Type, string AccountNumber, string Nickname, Guid? PropertyId, bool? IsDefault);

    private static async Task<ValidInput> ValidateAsync(
        GridDbContext db, Guid consumerId, UtilityAccountRequest body, CancellationToken ct)
    {
        var v = new Validator();
        if (body.Type is null) v.Add("type", "Choose a utility.");
        var nickname = v.Required("nickname", body.Nickname, 60, "Name");

        string? number = null;
        if (body.Type is { } type)
        {
            number = UtilityTypeRules.Normalize(type, body.AccountNumber, out var error);
            if (number is null)
            {
                // Its own code so the app can point at the number field specifically.
                throw new ApiException(400, "utility_accounts.invalid_number", error!,
                    fields: [new FieldError("accountNumber", error!)]);
            }
        }
        v.ThrowIfInvalid();

        if (body.PropertyId is { } propertyId)
        {
            var owned = await db.Properties.AnyAsync(
                p => p.Id == propertyId && p.OwnerConsumerId == consumerId && p.ArchivedAt == null, ct);
            if (!owned) throw Errors.NotFound("properties.not_found", "That home was not found.");
        }

        return new ValidInput(body.Type!.Value, number!, nickname, body.PropertyId, body.IsDefault);
    }
}
