using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Services;

public sealed class ManagedChatAuthorizationService(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<ManagedChatAuthorizationService> logger)
{
    public WhitelistMode GetWhitelistMode()
    {
        var modeStr = configuration["GLOBAL_WHITELIST_MODE"] ?? "Enforced";
        if (!Enum.TryParse<WhitelistMode>(modeStr, true, out var mode))
        {
            mode = WhitelistMode.Enforced;
        }
        return mode;
    }

    public async Task<bool> IsChatAuthorizedAsync(long chatId, string title, string chatType, CancellationToken ct)
    {
        var mode = GetWhitelistMode();
        if (mode == WhitelistMode.Disabled)
        {
            return true;
        }

        var managed = await db.ManagedChats.FindAsync([chatId], ct);

        if (mode == WhitelistMode.Audit)
        {
            if (managed == null)
            {
                managed = new ManagedChat
                {
                    ChatId = chatId,
                    Title = string.IsNullOrWhiteSpace(title) ? "Unknown Group" : title,
                    ChatType = string.IsNullOrWhiteSpace(chatType) ? "group" : chatType,
                    AuthorizationStatus = AuthorizationStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                db.ManagedChats.Add(managed);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Group {ChatId} ({Title}) registered as Pending under Audit mode.", chatId, managed.Title);
            }

            if (managed.AuthorizationStatus != AuthorizationStatus.Approved)
            {
                logger.LogWarning("Audit: Unauthorized access attempt from group {ChatId} ({Title}) with status {Status}.", chatId, managed.Title, managed.AuthorizationStatus);
            }

            return true;
        }

        // Enforced mode
        if (managed == null)
        {
            managed = new ManagedChat
            {
                ChatId = chatId,
                Title = string.IsNullOrWhiteSpace(title) ? "Unknown Group" : title,
                ChatType = string.IsNullOrWhiteSpace(chatType) ? "group" : chatType,
                AuthorizationStatus = AuthorizationStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.ManagedChats.Add(managed);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Enforced: Unregistered group {ChatId} ({Title}) registered as Pending and blocked.", chatId, managed.Title);
            return false;
        }

        if (managed.AuthorizationStatus == AuthorizationStatus.Approved)
        {
            return true;
        }

        logger.LogWarning("Enforced: Group {ChatId} ({Title}) access blocked due to status {Status}.", chatId, managed.Title, managed.AuthorizationStatus);
        return false;
    }
}
