using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Consumers;
using Microsoft.EntityFrameworkCore;

namespace Grid.Api.Features.Providers;

public sealed record ProviderContext(Consumer Consumer, ProviderAccount Account, ProviderApplication Application);

/// <summary>
/// The provider account owned by the signed-in consumer. Every provider endpoint starts here, so
/// an account is only ever reached through its owner.
/// </summary>
public sealed class CurrentProvider(CurrentConsumer current, GridDbContext db)
{
    public async Task<ProviderContext> LoadAsync(CancellationToken ct)
    {
        var consumer = await current.LoadAsync(ct);
        var account = await db.ProviderAccounts.SingleOrDefaultAsync(a => a.OwnerConsumerId == consumer.Id, ct)
                      ?? throw Errors.ProviderAccountNotFound();
        var application = await db.ProviderApplications.SingleAsync(a => a.ProviderAccountId == account.Id, ct);
        return new ProviderContext(consumer, account, application);
    }

    /// <summary>For changes to verification-critical details: refused while GRID reviews or has approved them.</summary>
    public async Task<ProviderContext> LoadEditableAsync(CancellationToken ct)
    {
        var context = await LoadAsync(ct);
        if (context.Application.IsLocked) throw Errors.ProviderLocked();
        return context;
    }

    /// <summary>The account a consumer owns, if any; what <c>user.providerAccountId</c> reports.</summary>
    internal static Task<Guid?> AccountIdForAsync(GridDbContext db, Guid consumerId, CancellationToken ct) =>
        db.ProviderAccounts.Where(a => a.OwnerConsumerId == consumerId).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct);
}
