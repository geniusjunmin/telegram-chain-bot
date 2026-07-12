using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;
using TelegramChainBot.Security;
using TelegramChainBot.Services;

namespace TelegramChainBot.Bot;

public sealed class UpdateHandler(
    ChainService chainService, 
    TelegramService telegramService, 
    AppDbContext db,
    GroupAdminValidator adminValidator,
    IConfiguration configuration,
    ILogger<UpdateHandler> logger)
{
    public async Task HandleAsync(Update update, CancellationToken cancellationToken)
    {
        logger.LogInformation("Received update type: {Type}, ID: {Id}", update.Type, update.Id.ToString());

        if (update.Type == UpdateType.Message && update.Message?.Text is not null)
        {
            await HandleMessageAsync(update.Message, cancellationToken);
            return;
        }

        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is not null)
        {
            logger.LogInformation("Handling CallbackQuery from User: {UserId}, Data: {Data}", 
                update.CallbackQuery.From.Id.ToString(), update.CallbackQuery.Data);
            await HandleCallbackAsync(update.CallbackQuery, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        var text = message.Text!.Trim();
        logger.LogInformation("Processing message: {Text}", text);

        // 1. Whitelist checking for group chats
        var chatType = message.Chat.Type;
        if (chatType == ChatType.Group || chatType == ChatType.Supergroup)
        {
            var managed = await db.ManagedChats.FindAsync([message.Chat.Id], cancellationToken);
            if (managed == null)
            {
                // Auto-register as Disabled
                managed = new ManagedChat
                {
                    ChatId = message.Chat.Id,
                    Title = message.Chat.Title ?? "Unknown Group",
                    Status = ManagedChatStatus.Disabled,
                    CreatedAt = DateTimeOffset.UtcNow,
                    AuthorizedBy = "System"
                };
                db.ManagedChats.Add(managed);
                await db.SaveChangesAsync(cancellationToken);
            }

            if (managed.Status == ManagedChatStatus.Disabled)
            {
                logger.LogWarning("Group {ChatId} ({Title}) is Disabled in Whitelist. Leaving chat.", message.Chat.Id, managed.Title);
                try
                {
                    await telegramService.LeaveChatAsync(message.Chat.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to leave chat {ChatId}.", message.Chat.Id);
                }
                return;
            }

            if (managed.Status == ManagedChatStatus.Audit)
            {
                logger.LogInformation("Group {ChatId} ({Title}) is in Audit mode. Logging message but ignoring commands.", message.Chat.Id, managed.Title);
                return;
            }
        }

        // 2. Parse Owner commands (Private chat only)
        if (text.StartsWith("/whitelist_add", StringComparison.OrdinalIgnoreCase))
        {
            await HandleWhitelistAddAsync(message, text, cancellationToken);
            return;
        }
        if (text.StartsWith("/whitelist_remove", StringComparison.OrdinalIgnoreCase))
        {
            await HandleWhitelistRemoveAsync(message, text, cancellationToken);
            return;
        }
        if (text.StartsWith("/whitelist_list", StringComparison.OrdinalIgnoreCase))
        {
            await HandleWhitelistListAsync(message, text, cancellationToken);
            return;
        }

        // 3. Parse Group Admin commands
        if (text.StartsWith("/delete_chain", StringComparison.OrdinalIgnoreCase))
        {
            await DeleteChainFromGroupAsync(message, text, cancellationToken);
            return;
        }

        // 4. Standard bot commands
        if (TryParseStartChainCommand(message, out var title))
        {
            await CreateChainAsync(message, title, cancellationToken);
            return;
        }

        if (TryParseJoinChainCommand(message, out var chainPublicId))
        {
            await SendJoinWebAppAsync(message, chainPublicId, cancellationToken);
            return;
        }
    }

    private bool IsBotOwner(long userId)
    {
        var ownerIdsStr = configuration["BOT_OWNER_IDS"] ?? string.Empty;
        var ownerIds = ownerIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                  .Select(long.Parse)
                                  .ToHashSet();
        return ownerIds.Contains(userId);
    }

    private async Task HandleWhitelistAddAsync(Message message, string text, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Private || message.From is null || !IsBotOwner(message.From.Id))
        {
            return;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "格式错误。用法：/whitelist_add <chatId> <status>", cancellationToken);
            return;
        }

        if (!long.TryParse(parts[1], out var targetChatId))
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "无效的 chatId。", cancellationToken);
            return;
        }

        var statusStr = parts[2];
        if (!Enum.TryParse<ManagedChatStatus>(statusStr, true, out var status))
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "无效的 status。可用状态：Disabled, Audit, Enforced", cancellationToken);
            return;
        }

        var managed = await db.ManagedChats.FindAsync([targetChatId], cancellationToken);
        if (managed == null)
        {
            managed = new ManagedChat
            {
                ChatId = targetChatId,
                Title = $"Group {targetChatId}",
                Status = status,
                CreatedAt = DateTimeOffset.UtcNow,
                AuthorizedBy = message.From.Username ?? message.From.Id.ToString()
            };
            db.ManagedChats.Add(managed);
        }
        else
        {
            managed.Status = status;
            managed.AuthorizedBy = message.From.Username ?? message.From.Id.ToString();
        }

        await db.SaveChangesAsync(cancellationToken);
        await telegramService.SendTextMessageAsync(message.Chat.Id, $"成功添加/更新白名单：群组 {targetChatId}，状态为 {status}。", cancellationToken);
    }

    private async Task HandleWhitelistRemoveAsync(Message message, string text, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Private || message.From is null || !IsBotOwner(message.From.Id))
        {
            return;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "格式错误。用法：/whitelist_remove <chatId>", cancellationToken);
            return;
        }

        if (!long.TryParse(parts[1], out var targetChatId))
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "无效的 chatId。", cancellationToken);
            return;
        }

        var managed = await db.ManagedChats.FindAsync([targetChatId], cancellationToken);
        if (managed == null)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "该群组不在白名单中。", cancellationToken);
            return;
        }

        db.ManagedChats.Remove(managed);
        await db.SaveChangesAsync(cancellationToken);
        await telegramService.SendTextMessageAsync(message.Chat.Id, $"成功从白名单移除群组 {targetChatId}。", cancellationToken);
    }

    private async Task HandleWhitelistListAsync(Message message, string text, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Private || message.From is null || !IsBotOwner(message.From.Id))
        {
            return;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var page = 1;
        if (parts.Length >= 2 && int.TryParse(parts[1], out var parsedPage))
        {
            page = Math.Max(1, parsedPage);
        }

        const int pageSize = 10;
        var total = await db.ManagedChats.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling((double)total / pageSize);

        var list = await db.ManagedChats
            .OrderBy(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        if (list.Count == 0)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "白名单为空。", cancellationToken);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- 白名单群组列表 (第 {page}/{totalPages} 页，总数 {total}) ---");
        foreach (var chat in list)
        {
            sb.AppendLine($"ID: `{chat.ChatId}` | 标题: {chat.Title} | 状态: {chat.Status} | 授权人: {chat.AuthorizedBy}");
        }
        sb.AppendLine();
        sb.AppendLine("使用 `/whitelist_list <页码>` 查看其他页。");

        await telegramService.SendTextMessageAsync(message.Chat.Id, sb.ToString(), cancellationToken);
    }

    private async Task DeleteChainFromGroupAsync(Message message, string text, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Group && message.Chat.Type != ChatType.Supergroup)
        {
            return;
        }

        var userId = message.From?.Id ?? 0;
        var isGroupAdmin = await adminValidator.IsAdminOrOwnerAsync(message.Chat.Id, userId, cancellationToken);
        if (!isGroupAdmin)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "只有群管理员可以删除接龙。", cancellationToken);
            return;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "格式错误。用法：/delete_chain <publicId>", cancellationToken);
            return;
        }

        var publicId = parts[1];
        var chain = await chainService.GetChainByPublicIdAsync(publicId, cancellationToken);
        if (chain == null || chain.ChatId != message.Chat.Id)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "接龙不存在或不属于当前群组。", cancellationToken);
            return;
        }

        await chainService.DeleteChainAsync(chain.Id, cancellationToken);
        await telegramService.SendTextMessageAsync(message.Chat.Id, $"管理员已删除接龙 \"{chain.Title}\"。", cancellationToken);
    }

    private async Task CreateChainAsync(Message message, string title, CancellationToken cancellationToken)
    {
        title = SanitizeChainTitle(title);

        if (string.IsNullOrWhiteSpace(title) || title.StartsWith("@"))
        {
            title = "聚餐接龙";
        }

        var creatorId = message.From?.Id ?? 0;
        logger.LogInformation("Creating chain with Title: {Title}, Creator: {CreatorId}", title, creatorId);

        var chain = await chainService.CreateChainAsync(title, creatorId, cancellationToken);

        try
        {
            var output = ChainService.FormatChainMessage(title, []);
            var (chatId, messageId) = await telegramService.SendChainMessageAsync(message.Chat.Id, chain.PublicId, output, cancellationToken);
            await chainService.SetMessageInfoAsync(chain.Id, chatId, messageId, cancellationToken);
        }
        catch
        {
            await chainService.DeleteChainAsync(chain.Id, cancellationToken);
            throw;
        }
    }

    private async Task SendJoinWebAppAsync(Message message, string chainPublicId, CancellationToken cancellationToken)
    {
        if (message.Chat.Type is not ChatType.Private)
        {
            return;
        }

        var chain = await chainService.GetChainByPublicIdAsync(chainPublicId, cancellationToken);
        if (chain is null)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "这个接龙不存在或已失效。", cancellationToken);
            return;
        }

        await telegramService.SendOpenChainWebAppAsync(message.Chat.Id, chain.PublicId, chain.Title, cancellationToken);
    }

    private static bool TryParseStartChainCommand(Message message, out string title)
    {
        title = string.Empty;
        var text = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (TryParseCommandEntity(message, text, out title))
        {
            return true;
        }

        return TryParseCommandFromPlainText(text, out title);
    }

    private static bool TryParseCommandEntity(Message message, string text, out string title)
    {
        title = string.Empty;

        var commandEntity = message.Entities?
            .FirstOrDefault(entity => entity.Type == MessageEntityType.BotCommand && entity.Offset == 0);

        if (commandEntity is null)
        {
            return false;
        }

        var commandText = text.Substring(commandEntity.Offset, commandEntity.Length);
        if (!commandText.StartsWith("/start_chain", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        title = text.Length > commandEntity.Length
            ? text[commandEntity.Length..].Trim()
            : string.Empty;

        return true;
    }

    private static bool TryParseCommandFromPlainText(string text, out string title)
    {
        title = string.Empty;

        const string command = "/start_chain";
        var commandIndex = text.IndexOf(command, StringComparison.OrdinalIgnoreCase);
        if (commandIndex < 0)
        {
            return false;
        }

        title = text[(commandIndex + command.Length)..].Trim();
        return true;
    }

    private static bool TryParseJoinChainCommand(Message message, out string chainPublicId)
    {
        chainPublicId = string.Empty;
        var text = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var commandEntity = message.Entities?
            .FirstOrDefault(entity => entity.Type == MessageEntityType.BotCommand && entity.Offset == 0);

        if (commandEntity is null)
        {
            return false;
        }

        var commandText = text.Substring(commandEntity.Offset, commandEntity.Length);
        if (!commandText.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = text.Length > commandEntity.Length
            ? text[commandEntity.Length..].Trim()
            : string.Empty;

        if (!payload.StartsWith("join_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        chainPublicId = payload["join_".Length..];
        return !string.IsNullOrWhiteSpace(chainPublicId);
    }

    private static string SanitizeChainTitle(string title)
    {
        var trimmed = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && parts[^1].StartsWith("@", StringComparison.Ordinal))
        {
            return string.Join(' ', parts[..^1]);
        }

        return trimmed;
    }

    private async Task HandleCallbackAsync(CallbackQuery callback, CancellationToken cancellationToken)
    {
        try 
        {
            if (string.IsNullOrWhiteSpace(callback.Data) || !callback.Data.StartsWith("join:", StringComparison.OrdinalIgnoreCase))
            {
                await telegramService.AnswerCallbackAsync(callback.Id, "无效的操作", cancellationToken);
                return;
            }

            var publicId = callback.Data.Split(':', 2)[1];

            // Whitelist check for CallbackQuery
            if (callback.Message != null)
            {
                var chatType = callback.Message.Chat.Type;
                if (chatType == ChatType.Group || chatType == ChatType.Supergroup)
                {
                    var managed = await db.ManagedChats.FindAsync([callback.Message.Chat.Id], cancellationToken);
                    if (managed == null || managed.Status != ManagedChatStatus.Enforced)
                    {
                        await telegramService.AnswerCallbackAsync(callback.Id, "群聊未授权使用此 Bot", cancellationToken);
                        return;
                    }
                }
            }

            var chain = await chainService.GetChainByPublicIdAsync(publicId, cancellationToken);
            if (chain is null)
            {
                await telegramService.AnswerCallbackAsync(callback.Id, "接龙不存在", cancellationToken);
                return;
            }

            var user = callback.From;
            var telegramNickname = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(telegramNickname))
            {
                telegramNickname = user.Username ?? $"user_{user.Id}";
            }

            var (added, members) = await chainService.JoinAsync(chain.Id, user.Id, telegramNickname, telegramNickname, cancellationToken);
            
            await telegramService.AnswerCallbackAsync(callback.Id, added ? "加入成功" : "你已经参加过了", cancellationToken);

            if (added)
            {
                var messageText = ChainService.FormatChainMessage(chain.Title, members);
                await telegramService.EditChainMessageAsync(chain.ChatId, chain.MessageId, chain.PublicId, messageText, cancellationToken);
            }
        }
        catch (Exception)
        {
            try { await telegramService.AnswerCallbackAsync(callback.Id, "出错了，请稍后再试", cancellationToken); } catch {}
        }
    }
}
