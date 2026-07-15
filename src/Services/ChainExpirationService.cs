using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Services;

public sealed class ChainExpirationService(
    IServiceProvider serviceProvider,
    BackgroundWorkerTracker tracker,
    ILogger<ChainExpirationService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ChainExpirationService is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                tracker.LastExpirationCheck = DateTimeOffset.UtcNow;
                await CheckExpiredChainsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during checking expired chains.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("ChainExpirationService is stopping.");
    }

    private async Task CheckExpiredChainsAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tg = scope.ServiceProvider.GetRequiredService<TelegramService>();

        var now = DateTimeOffset.UtcNow;
        var expiredChains = await db.Chains
            .Where(c => c.Status == ChainStatus.Active && c.ExpiresAt != null && c.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expiredChains.Count == 0)
        {
            return;
        }

        logger.LogInformation("Found {Count} expired chains to transition.", expiredChains.Count);

        foreach (var chain in expiredChains)
        {
            try
            {
                chain.Status = ChainStatus.Expired;
                chain.UpdatedAt = now;
                await db.SaveChangesAsync(ct);

                logger.LogInformation("Transitioned chain {ChainId} to Expired. Triggering Telegram sync.", chain.Id);
                await tg.SyncChainMessageAsync(chain.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to transition and sync expired chain {ChainId}.", chain.Id);
            }
        }
    }
}
