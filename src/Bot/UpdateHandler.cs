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

        var chatType = message.Chat.Type;
        if (chatType == ChatType.Group || chatType == ChatType.Supergroup)
        {
            var modeStr = configuration["GLOBAL_WHITELIST_MODE"] ?? "Enforced";
            if (!Enum.TryParse<WhitelistMode>(modeStr, true, out var mode))
            {
                mode = WhitelistMode.Enforced;
            }

            if (mode != WhitelistMode.Disabled)
            {
                var managed = await db.ManagedChats.FindAsync([message.Chat.Id], cancellationToken);
                
                if (mode == WhitelistMode.Audit)
                {
                    if (managed == null)
                    {
                        managed = new ManagedChat
                        {
                            ChatId = message.Chat.Id,
                            Title = message.Chat.Title ?? "Unknown Group",
                            ChatType = chatType == ChatType.Supergroup ? "supergroup" : "group",
                            AuthorizationStatus = AuthorizationStatus.Pending,
                            CreatedAt = DateTimeOffset.UtcNow,
                            UpdatedAt = DateTimeOffset.UtcNow
                        };
                        db.ManagedChats.Add(managed);
                        await db.SaveChangesAsync(cancellationToken);
                        
                        logger.LogInformation("Group {ChatId} ({Title}) registered as Pending under Audit mode.", message.Chat.Id, managed.Title);
                    }
                }
                else if (mode == WhitelistMode.Enforced)
                {
                    if (managed == null || managed.AuthorizationStatus == AuthorizationStatus.Pending)
                    {
                        if (managed == null)
                        {
                            managed = new ManagedChat
                            {
                                ChatId = message.Chat.Id,
                                Title = message.Chat.Title ?? "Unknown Group",
                                ChatType = chatType == ChatType.Supergroup ? "supergroup" : "group",
                                AuthorizationStatus = AuthorizationStatus.Pending,
                                CreatedAt = DateTimeOffset.UtcNow,
                                UpdatedAt = DateTimeOffset.UtcNow
                            };
                            db.ManagedChats.Add(managed);
                            await db.SaveChangesAsync(cancellationToken);
                        }
                        
                        await telegramService.SendTextMessageAsync(message.Chat.Id, "该群聊尚未获得授权，请联系管理员。", cancellationToken);
                        return;
                    }

                    if (managed.AuthorizationStatus == AuthorizationStatus.Blocked)
                    {
                        logger.LogWarning("Group {ChatId} is Blocked. Leaving group.", message.Chat.Id);
                        try
                        {
                            await telegramService.LeaveChatAsync(message.Chat.Id, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to leave blocked group {ChatId}.", message.Chat.Id);
                        }
                        return;
                    }
                }
            }
        }

        if (TryMatchCommand(message, "/whitelist_add", out var addArg))
        {
            await HandleWhitelistAddAsync(message, addArg, cancellationToken);
            return;
        }
        if (TryMatchCommand(message, "/whitelist_remove", out var removeArg))
        {
            await HandleWhitelistRemoveAsync(message, removeArg, cancellationToken);
            return;
        }
        if (TryMatchCommand(message, "/whitelist_list", out var listArg))
        {
            await HandleWhitelistListAsync(message, listArg, cancellationToken);
            return;
        }

        if (TryMatchCommand(message, "/delete_chain", out var deleteArg))
        {
            await DeleteChainFromGroupAsync(message, deleteArg, cancellationToken);
            return;
        }

        if (TryMatchCommand(message, "/start_chain", out var startArg))
        {
            await CreateChainAsync(message, startArg, cancellationToken);
            return;
        }

        if (TryMatchCommand(message, "/close_chain", out _))
        {
            await CloseChainFromGroupAsync(message, cancellationToken);
            return;
        }

        if (TryMatchCommand(message, "/leave_chain", out _))
        {
            await LeaveChainFromGroupAsync(message, cancellationToken);
            return;
        }

        if (TryMatchCommand(message, "/chain_settings", out _))
        {
            await ShowChainSettingsAsync(message, cancellationToken);
            return;
        }

        if (TryMatchCommand(message, "/chain_admin_only", out var adminOnlyArg))
        {
            await SetChainAdminOnlyAsync(message, adminOnlyArg, cancellationToken);
            return;
        }

        if (TryMatchCommand(message, "/help", out _))
        {
            await ShowHelpAsync(message, cancellationToken);
            return;
        }

        if (TryParseJoinChainCommand(message, out var chainPublicId))
        {
            await SendJoinWebAppAsync(message, chainPublicId, cancellationToken);
            return;
        }
    }

    private static bool TryMatchCommand(Message message, string commandName, out string argument)
    {
        argument = string.Empty;
        var text = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;

        var commandEntity = message.Entities?
            .FirstOrDefault(entity => entity.Type == MessageEntityType.BotCommand && entity.Offset == 0);

        if (commandEntity is null) return false;

        var commandText = text.Substring(commandEntity.Offset, commandEntity.Length);
        
        var atIndex = commandText.IndexOf('@');
        var baseCommand = atIndex >= 0 ? commandText[..atIndex] : commandText;

        if (!string.Equals(baseCommand, commandName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        argument = text.Length > commandEntity.Length
            ? text[commandEntity.Length..].Trim()
            : string.Empty;

        return true;
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
        if (parts.Length < 1)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "格式错误。用法：/whitelist_add <chatId> [status]", cancellationToken);
            return;
        }

        if (!long.TryParse(parts[0], out var targetChatId))
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "无效的 chatId。", cancellationToken);
            return;
        }

        var status = AuthorizationStatus.Approved;
        if (parts.Length >= 2 && Enum.TryParse<AuthorizationStatus>(parts[1], true, out var parsedStatus))
        {
            status = parsedStatus;
        }

        var managed = await db.ManagedChats.FindAsync([targetChatId], cancellationToken);
        if (managed == null)
        {
            managed = new ManagedChat
            {
                ChatId = targetChatId,
                Title = $"Group {targetChatId}",
                ChatType = "supergroup",
                AuthorizationStatus = status,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.ManagedChats.Add(managed);
        }
        else
        {
            managed.AuthorizationStatus = status;
            managed.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (status == AuthorizationStatus.Approved)
        {
            managed.ApprovedAt = DateTimeOffset.UtcNow;
        }
        else if (status == AuthorizationStatus.Blocked)
        {
            managed.BlockedAt = DateTimeOffset.UtcNow;
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
        if (parts.Length < 1)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "格式错误。用法：/whitelist_remove <chatId>", cancellationToken);
            return;
        }

        if (!long.TryParse(parts[0], out var targetChatId))
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

        managed.AuthorizationStatus = AuthorizationStatus.Blocked;
        managed.BlockedAt = DateTimeOffset.UtcNow;
        managed.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await telegramService.SendTextMessageAsync(message.Chat.Id, $"成功将群组 {targetChatId} 标记为 Blocked。", cancellationToken);
    }

    private async Task HandleWhitelistListAsync(Message message, string text, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Private || message.From is null || !IsBotOwner(message.From.Id))
        {
            return;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var page = 1;
        if (parts.Length >= 1 && int.TryParse(parts[0], out var parsedPage))
        {
            page = Math.Max(1, parsedPage);
        }

        var total = await db.ManagedChats.CountAsync(cancellationToken);
        var pageSize = 10;
        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        if (totalPages == 0) totalPages = 1;

        page = Math.Min(page, totalPages);

        var list = await db.ManagedChats
            .OrderBy(c => c.ChatId)
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
            sb.AppendLine($"ID: `{chat.ChatId}` | 标题: {chat.Title} | 状态: {chat.AuthorizationStatus} | 类型: {chat.ChatType}");
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
        if (parts.Length < 1)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "格式错误。用法：/delete_chain <publicId>", cancellationToken);
            return;
        }

        var publicId = parts[0];
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
        var managed = await db.ManagedChats.FindAsync([message.Chat.Id], cancellationToken);
        if (managed == null || managed.AuthorizationStatus != AuthorizationStatus.Approved)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "该群聊尚未获得授权，请联系管理员。", cancellationToken);
            return;
        }

        var creatorId = message.From?.Id ?? 0;
        if (managed.CreatePolicy == CreatePolicy.ChatAdministrators)
        {
            var isGroupAdmin = await adminValidator.IsAdminOrOwnerAsync(message.Chat.Id, creatorId, cancellationToken) || IsBotOwner(creatorId);
            if (!isGroupAdmin)
            {
                await telegramService.SendTextMessageAsync(message.Chat.Id, "只有群管理员才能发起接龙。", cancellationToken);
                return;
            }
        }
        else if (managed.CreatePolicy == CreatePolicy.BotOwners)
        {
            if (!IsBotOwner(creatorId))
            {
                await telegramService.SendTextMessageAsync(message.Chat.Id, "只有 Bot 拥有者才能发起接龙。", cancellationToken);
                return;
            }
        }
        else if (managed.CreatePolicy == CreatePolicy.Disabled)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "本群已禁止发起新接龙。", cancellationToken);
            return;
        }

        var activeCount = await db.Chains.CountAsync(c => c.ChatId == message.Chat.Id && c.Status == ChainStatus.Active && !c.DeletedAt.HasValue, cancellationToken);
        if (activeCount >= managed.MaxActiveChains)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, $"本群活动接龙已达上限（最大 {managed.MaxActiveChains} 个）。", cancellationToken);
            return;
        }

        title = SanitizeChainTitle(title);
        if (string.IsNullOrWhiteSpace(title) || title.StartsWith("@"))
        {
            title = "聚餐接龙";
        }

        if (title.Length > 100)
        {
            title = title[..100];
        }

        logger.LogInformation("Creating chain with Title: {Title}, Creator: {CreatorId}", title, creatorId);

        var chain = await chainService.CreateChainAsync(title, creatorId, cancellationToken);

        try
        {
            var output = ChainService.FormatChainMessage(title, []);
            var (chatId, messageId) = await telegramService.SendChainMessageAsync(message.Chat.Id, chain.PublicId, output, cancellationToken);
            await chainService.SetMessageInfoAsync(chain.Id, chatId, messageId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send chain message to Telegram.");
            await chainService.DeleteChainAsync(chain.Id, cancellationToken);
            throw;
        }
    }

    private async Task CloseChainFromGroupAsync(Message message, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Group && message.Chat.Type != ChatType.Supergroup) return;

        var chain = await db.Chains.FirstOrDefaultAsync(c => c.ChatId == message.Chat.Id && c.Status == ChainStatus.Active && !c.DeletedAt.HasValue, cancellationToken);
        if (chain == null)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "当前群组没有活动的接龙。", cancellationToken);
            return;
        }

        var userId = message.From?.Id ?? 0;
        var isCreator = chain.CreatorTelegramUserId == userId;
        var isGroupAdmin = await adminValidator.IsAdminOrOwnerAsync(message.Chat.Id, userId, cancellationToken) || IsBotOwner(userId);

        if (!isCreator && !isGroupAdmin)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "只有接龙发起人或群管理员可以关闭接龙。", cancellationToken);
            return;
        }

        chain.Status = ChainStatus.Closed;
        chain.ClosedAt = DateTimeOffset.UtcNow;
        chain.ClosedByTelegramUserId = userId;
        chain.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var members = await chainService.GetMembersAsync(chain.Id, cancellationToken);
        var messageText = ChainService.FormatChainMessage(chain.Title, members) + "\n\n⚠️ 接龙已关闭。";
        await telegramService.EditChainMessageAsync(chain.ChatId, chain.MessageId.GetValueOrDefault(), chain.PublicId, messageText, cancellationToken);
        await telegramService.SendTextMessageAsync(message.Chat.Id, $"接龙 \"{chain.Title}\" 已成功关闭。", cancellationToken);
    }

    private async Task LeaveChainFromGroupAsync(Message message, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Group && message.Chat.Type != ChatType.Supergroup) return;

        var chain = await db.Chains.FirstOrDefaultAsync(c => c.ChatId == message.Chat.Id && c.Status == ChainStatus.Active && !c.DeletedAt.HasValue, cancellationToken);
        if (chain == null)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "当前群组没有活动的接龙。", cancellationToken);
            return;
        }

        var userId = message.From?.Id ?? 0;
        var (removed, members, error) = await chainService.LeaveAsync(chain.Id, userId, cancellationToken);
        if (error != null)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, $"退出失败: {error}", cancellationToken);
            return;
        }

        if (removed)
        {
            var messageText = ChainService.FormatChainMessage(chain.Title, members);
            await telegramService.EditChainMessageAsync(chain.ChatId, chain.MessageId.GetValueOrDefault(), chain.PublicId, messageText, cancellationToken);
            await telegramService.SendTextMessageAsync(message.Chat.Id, "你已成功退出接龙。", cancellationToken);
        }
        else
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "你尚未加入该接龙。", cancellationToken);
        }
    }

    private async Task ShowChainSettingsAsync(Message message, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Group && message.Chat.Type != ChatType.Supergroup) return;

        var managed = await db.ManagedChats.FindAsync([message.Chat.Id], cancellationToken);
        if (managed == null) return;

        var policyText = managed.CreatePolicy switch
        {
            CreatePolicy.ChatAdministrators => "仅群管理员 (AdminOnly)",
            CreatePolicy.Everyone => "所有群成员 (AllMembers)",
            CreatePolicy.BotOwners => "仅Bot拥有者",
            CreatePolicy.Disabled => "禁止创建接龙",
            _ => managed.CreatePolicy.ToString()
        };

        var response = $"--- 群接龙设置 ---\n" +
                       $"允许加入接龙: {(managed.IsJoinEnabled ? "是" : "否")}\n" +
                       $"默认最大人数: {managed.DefaultMaxMembers}\n" +
                       $"最大活动接龙数: {managed.MaxActiveChains}\n" +
                       $"创建策略: {policyText}";

        await telegramService.SendTextMessageAsync(message.Chat.Id, response, cancellationToken);
    }

    private async Task SetChainAdminOnlyAsync(Message message, string arg, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Group && message.Chat.Type != ChatType.Supergroup) return;

        var userId = message.From?.Id ?? 0;
        var isGroupAdmin = await adminValidator.IsAdminOrOwnerAsync(message.Chat.Id, userId, cancellationToken) || IsBotOwner(userId);
        if (!isGroupAdmin)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "只有群管理员可以修改设置。", cancellationToken);
            return;
        }

        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || (!string.Equals(parts[0], "on", StringComparison.OrdinalIgnoreCase) && !string.Equals(parts[0], "off", StringComparison.OrdinalIgnoreCase)))
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "格式错误。用法：/chain_admin_only on|off", cancellationToken);
            return;
        }

        var policy = string.Equals(parts[0], "on", StringComparison.OrdinalIgnoreCase) ? CreatePolicy.ChatAdministrators : CreatePolicy.Everyone;
        var managed = await db.ManagedChats.FindAsync([message.Chat.Id], cancellationToken);
        if (managed != null)
        {
            managed.CreatePolicy = policy;
            managed.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        var policyText = policy == CreatePolicy.ChatAdministrators ? "仅限群管理员创建接龙" : "所有群成员均可创建接龙";
        await telegramService.SendTextMessageAsync(message.Chat.Id, $"接龙创建策略已更新：{policyText}。", cancellationToken);
    }

    private async Task ShowHelpAsync(Message message, CancellationToken cancellationToken)
    {
        var helpText = "--- 接龙机器人使用帮助 ---\n\n" +
                       "群聊可用命令：\n" +
                       "/start_chain <标题> - 发起新接龙\n" +
                       "/close_chain - 关闭当前活动接龙 (仅创建者或群管理员)\n" +
                       "/leave_chain - 退出当前活动接龙\n" +
                       "/delete_chain <publicId> - 删除接龙 (仅群管理员)\n" +
                       "/chain_settings - 查看本群接龙配置\n" +
                       "/chain_admin_only on|off - 修改接龙发起权限 (仅群管理员)\n" +
                       "/help - 显示此帮助信息\n\n" +
                       "私聊可用命令：\n" +
                       "/start join_<publicId> - 私聊填写名字加入接龙";

        await telegramService.SendTextMessageAsync(message.Chat.Id, helpText, cancellationToken);
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
            if (string.IsNullOrWhiteSpace(callback.Data))
            {
                await telegramService.AnswerCallbackAsync(callback.Id, "无效的操作", cancellationToken);
                return;
            }

            var parts = callback.Data.Split(':', 2);
            if (parts.Length < 2)
            {
                await telegramService.AnswerCallbackAsync(callback.Id, "无效的数据格式", cancellationToken);
                return;
            }

            var action = parts[0].ToLowerInvariant();
            var publicId = parts[1];

            if (callback.Message != null)
            {
                var chatType = callback.Message.Chat.Type;
                if (chatType == ChatType.Group || chatType == ChatType.Supergroup)
                {
                    var managed = await db.ManagedChats.FindAsync([callback.Message.Chat.Id], cancellationToken);
                    if (managed == null || managed.AuthorizationStatus != AuthorizationStatus.Approved)
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

            if (action == "join")
            {
                var (added, members, error) = await chainService.JoinAsync(chain.Id, user.Id, telegramNickname, telegramNickname, cancellationToken);
                if (error != null)
                {
                    await telegramService.AnswerCallbackAsync(callback.Id, error, cancellationToken);
                    return;
                }
                
                await telegramService.AnswerCallbackAsync(callback.Id, added ? "加入成功" : "你已经参加过了", cancellationToken);

                if (added)
                {
                    var messageText = ChainService.FormatChainMessage(chain.Title, members);
                    await telegramService.EditChainMessageAsync(chain.ChatId, chain.MessageId.GetValueOrDefault(), chain.PublicId, messageText, cancellationToken);
                }
            }
            else if (action == "leave")
            {
                var (removed, members, error) = await chainService.LeaveAsync(chain.Id, user.Id, cancellationToken);
                if (error != null)
                {
                    await telegramService.AnswerCallbackAsync(callback.Id, error, cancellationToken);
                    return;
                }

                await telegramService.AnswerCallbackAsync(callback.Id, removed ? "已退出接龙" : "你尚未参加此接龙", cancellationToken);

                if (removed)
                {
                    var messageText = ChainService.FormatChainMessage(chain.Title, members);
                    await telegramService.EditChainMessageAsync(chain.ChatId, chain.MessageId.GetValueOrDefault(), chain.PublicId, messageText, cancellationToken);
                }
            }
            else if (action == "close")
            {
                var isCreator = chain.CreatorTelegramUserId == user.Id;
                var isGroupAdmin = false;
                if (callback.Message != null)
                {
                    isGroupAdmin = await adminValidator.IsAdminOrOwnerAsync(callback.Message.Chat.Id, user.Id, cancellationToken);
                }
                var isBotOwner = IsBotOwner(user.Id);
                var isBgAdmin = await db.AdminAccounts.AnyAsync(a => a.Username == user.Username && a.IsActive, cancellationToken);

                if (!isCreator && !isGroupAdmin && !isBotOwner && !isBgAdmin)
                {
                    await telegramService.AnswerCallbackAsync(callback.Id, "仅创建者或群管理员或系统管理员可以关闭", cancellationToken);
                    return;
                }

                chain.Status = ChainStatus.Closed;
                chain.ClosedAt = DateTimeOffset.UtcNow;
                chain.ClosedByTelegramUserId = user.Id;
                chain.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);

                var members = await chainService.GetMembersAsync(chain.Id, cancellationToken);
                var messageText = ChainService.FormatChainMessage(chain.Title, members) + "\n\n⚠️ 接龙已关闭。";
                await telegramService.EditChainMessageAsync(chain.ChatId, chain.MessageId.GetValueOrDefault(), chain.PublicId, messageText, cancellationToken);
                await telegramService.AnswerCallbackAsync(callback.Id, "接龙已关闭", cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle callback query.");
            try { await telegramService.AnswerCallbackAsync(callback.Id, "出错了，请稍后再试", cancellationToken); } catch {}
        }
    }
}
