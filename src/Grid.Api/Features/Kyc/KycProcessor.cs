using Grid.Api.Common;
using Grid.Api.Data;
using Grid.Api.Features.Consumers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Kyc;

/// <summary>
/// Works through consumers still waiting on a telco name lookup. The queue is the Consumers table
/// itself: <c>NotChecked</c> rows, and <c>Pending</c> rows whose next attempt is due.
/// </summary>
public sealed class KycProcessor(
    GridDbContext db,
    IIdentityLookup lookup,
    IOptions<KycOptions> options,
    TimeProvider time,
    ILogger<KycProcessor> logger)
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2), TimeSpan.FromHours(12),
    ];

    /// <summary>How long a claimed row stays out of other workers' way while its lookup runs.</summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>Processes one batch. Returns how many consumers were looked up.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        if (!lookup.Enabled) return 0;

        var config = options.Value;
        var now = time.GetUtcNow();
        var due = await db.Consumers
            .Where(c => c.KycStatus == KycStatus.NotChecked
                        || (c.KycStatus == KycStatus.Pending && (c.KycNextAttemptAt == null || c.KycNextAttemptAt <= now)))
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.Id)
            .Take(config.BatchSize)
            .ToListAsync(ct);

        var processed = 0;
        foreach (var id in due)
        {
            if (await ClaimAsync(id, now, ct))
            {
                await LookUpAsync(id, ct);
                processed++;
            }
        }
        return processed;
    }

    private async Task<bool> ClaimAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var leaseUntil = now + Lease;
        var claimed = await db.Consumers
            .Where(c => c.Id == id
                        && (c.KycStatus == KycStatus.NotChecked
                            || (c.KycStatus == KycStatus.Pending && (c.KycNextAttemptAt == null || c.KycNextAttemptAt <= now))))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.KycStatus, KycStatus.Pending)
                .SetProperty(c => c.KycNextAttemptAt, leaseUntil), ct);
        return claimed == 1;
    }

    private async Task LookUpAsync(Guid id, CancellationToken ct)
    {
        var consumer = await db.Consumers.SingleAsync(c => c.Id == id, ct);
        var result = await lookup.LookupNameAsync(consumer.PhoneNumber, ct);
        var now = time.GetUtcNow();

        consumer.KycAttempts++;
        consumer.KycProvider = lookup.Provider;

        switch (result.Outcome)
        {
            case IdentityLookupOutcome.Found:
                consumer.KycStatus = KycStatus.Verified;
                consumer.KycName = result.Name;
                consumer.KycCheckedAt = now;
                consumer.KycNextAttemptAt = null;
                consumer.KycLastError = null;
                break;

            case IdentityLookupOutcome.NotFound:
                consumer.KycStatus = KycStatus.NotFound;
                consumer.KycCheckedAt = now;
                consumer.KycNextAttemptAt = null;
                consumer.KycLastError = Clip(result.Error);
                break;

            default:
                consumer.KycLastError = Clip(result.Error);
                if (consumer.KycAttempts >= options.Value.MaxAttempts)
                {
                    consumer.KycStatus = KycStatus.Failed;
                    consumer.KycCheckedAt = now;
                    consumer.KycNextAttemptAt = null;
                }
                else
                {
                    consumer.KycStatus = KycStatus.Pending;
                    consumer.KycNextAttemptAt = now + Backoff[Math.Min(consumer.KycAttempts, Backoff.Length) - 1];
                }
                break;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("KYC lookup for {Phone}: {Outcome} (attempt {Attempt})",
            PhoneNumbers.Mask(consumer.PhoneNumber), result.Outcome, consumer.KycAttempts);
    }

    private static string? Clip(string? s) => s is { Length: > 500 } ? s[..500] : s;
}

/// <summary>Runs <see cref="KycProcessor"/> in the API process.</summary>
public sealed class KycWorker(IServiceScopeFactory scopes, IOptions<KycOptions> options, ILogger<KycWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.RunWorker || config.Provider == KycProviderKind.None)
        {
            logger.LogInformation("KYC worker not running (provider {Provider}, RunWorker {RunWorker})",
                config.Provider, config.RunWorker);
            return;
        }

        var idle = TimeSpan.FromSeconds(Math.Max(1, config.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                using var scope = scopes.CreateScope();
                processed = await scope.ServiceProvider.GetRequiredService<KycProcessor>().ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "KYC batch failed");
            }

            // A full batch probably means more is waiting; otherwise rest.
            if (processed < config.BatchSize)
                await Task.Delay(idle, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
