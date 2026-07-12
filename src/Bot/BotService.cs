using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Bot;

public sealed class BotService(
    UpdateHandler updateHandler, 
    AppDbContext db,
    ILogger<BotService> logger)
{
    public async Task HandleWebhookAsync(Update update, CancellationToken cancellationToken)
    {
        var existing = await db.ProcessedTelegramUpdates
            .FirstOrDefaultAsync(x => x.UpdateId == update.Id, cancellationToken);

        ProcessedTelegramUpdate record;

        if (existing != null)
        {
            if (existing.Status == "Processed")
            {
                logger.LogInformation("Telegram Update {UpdateId} has already been processed successfully.", update.Id);
                return;
            }
            if (existing.Status == "Processing")
            {
                logger.LogInformation("Telegram Update {UpdateId} is currently processing.", update.Id);
                return;
            }
            if (existing.Status == "Failed" && existing.AttemptCount >= 3)
            {
                logger.LogWarning("Telegram Update {UpdateId} failed and reached max retry attempts. Skipping.", update.Id);
                return;
            }

            // Allow retry if failed and attempts < 3
            existing.Status = "Processing";
            existing.AttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            record = existing;
        }
        else
        {
            record = new ProcessedTelegramUpdate
            {
                UpdateId = update.Id,
                ReceivedAt = DateTimeOffset.UtcNow,
                Status = "Processing",
                AttemptCount = 1
            };

            db.ProcessedTelegramUpdates.Add(record);
            
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                logger.LogWarning("Concurrent Telegram Update {UpdateId} detected during insert. Skipping.", update.Id);
                return;
            }
        }

        try
        {
            await updateHandler.HandleAsync(update, cancellationToken);
            
            record.Status = "Processed";
            record.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred processing Telegram Update {UpdateId}.", update.Id);
            
            record.Status = "Failed";
            record.LastError = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
            await db.SaveChangesAsync(cancellationToken);
            
            throw; // Rethrow to let the webhook return non-200 and trigger retry
        }
    }
}
