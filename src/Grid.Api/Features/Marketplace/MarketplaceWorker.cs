using Grid.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Marketplace;

/// <summary>
/// The marketplace's clock: settles MoMo payments and refunds, lapses offers nobody answered,
/// sends the next round for requests still looking, and confirms finished jobs the customer never
/// got round to.
/// </summary>
public sealed class MarketplaceProcessor(
    GridDbContext db, Dispatcher dispatcher, JobService jobs, ServicePayments.ServicePaymentService payments, TimeProvider time)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var work = await payments.ProcessDueAsync(ct);
        db.ChangeTracker.Clear();

        var now = time.GetUtcNow();
        work += await db.JobOffers
            .Where(o => o.Status == OfferStatus.Pending && o.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OfferStatus.Expired)
                .SetProperty(o => o.UpdatedAt, now), ct);

        var due = await db.ServiceRequests
            .Where(r => r.Status == ServiceRequestStatus.Searching && r.NextDispatchAt != null && r.NextDispatchAt <= now)
            .OrderBy(r => r.NextDispatchAt).Select(r => r.Id).Take(50).ToListAsync(ct);
        foreach (var id in due)
        {
            await dispatcher.DispatchAsync(id, ct);
            work++;
        }

        var unconfirmed = await db.Jobs
            .Where(j => j.Stage == JobStage.AwaitingConfirmation && j.AutoConfirmAt != null && j.AutoConfirmAt <= now)
            .Take(50).ToListAsync(ct);
        foreach (var job in unconfirmed)
        {
            await jobs.ConfirmAsync(job, actorConsumerId: null, ct);
            work++;
        }
        return work;
    }
}

public sealed class MarketplaceWorker(
    IServiceScopeFactory scopes, IOptions<MarketplaceOptions> options, ILogger<MarketplaceWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.RunWorker)
        {
            logger.LogInformation("Marketplace worker not running (RunWorker false)");
            return;
        }

        var idle = TimeSpan.FromSeconds(config.PollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MarketplaceProcessor>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Marketplace pass failed");
            }

            try
            {
                await Task.Delay(idle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
