using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;
using TelegramChainBot.Options;

namespace TelegramChainBot.Services;

public class TelegramService(
    ITelegramBotClient botClient,
    IOptions<BotOptions> options,
    TelegramMessageSyncService syncService,
    IServiceProvider serviceProvider,
    ILogger<TelegramService> logger)
{
    private readonly BotOptions _options = options.Value;

    public virtual async Task SyncChainMessageAsync(long chainId, CancellationToken cancellationToken)
    {
        var lockKey = $"chain:{chainId}";
        await syncService.ExecuteLockedAsync(lockKey, async () =>
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var chain = await db.Chains.FindAsync([chainId], cancellationToken);
            if (chain == null || chain.Status == ChainStatus.Deleted)
            {
                return;
            }

            if (!chain.MessageId.HasValue)
            {
                return;
            }

            var members = await db.ChainMembers
                .Where(m => m.ChainId == chainId && m.Status == ChainMemberStatus.Active)
                .OrderBy(m => m.JoinedAt)
                .ToListAsync(cancellationToken);

            var baseText = TelegramMessageFormatter.FormatChainMessage(chain.Title, members);

            var suffix = chain.Status switch
            {
                ChainStatus.Closed => "\n\n⚠️ 接龙已关闭。",
                ChainStatus.Expired => "\n\n⏰ 接龙已过期。",
                ChainStatus.Cancelled => "\n\n❌ 接龙已取消。",
                _ => string.Empty
            };

            var syncTime = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss");
            var text = $"{baseText}{suffix}\n\n[同步于: {syncTime}]";

            var replyMarkup = (chain.Status == ChainStatus.Closed ||
                               chain.Status == ChainStatus.Expired ||
                               chain.Status == ChainStatus.Cancelled)
                              ? null
                              : await BuildReplyMarkupAsync(chain.ChatId, chain.PublicId, cancellationToken);

            int maxRetries = 3;
            int delaySeconds = 1;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken);

                    await botClient.EditMessageText(
                        chatId: chain.ChatId,
                        messageId: (int)chain.MessageId.Value,
                        text: text,
                        parseMode: ParseMode.Html,
                        replyMarkup: replyMarkup,
                        cancellationToken: cancellationToken);

                    chain.TelegramSyncStatus = TelegramSyncStatus.Synced;
                    chain.LastSyncedAt = DateTimeOffset.UtcNow;
                    chain.LastSyncError = null;
                    await db.SaveChangesAsync(cancellationToken);

                    logger.LogInformation("Successfully synced chain {ChainId} message.", chainId);
                    break;
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.Message.Contains("message is not modified"))
                {
                    chain.TelegramSyncStatus = TelegramSyncStatus.Synced;
                    chain.LastSyncedAt = DateTimeOffset.UtcNow;
                    chain.LastSyncError = null;
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.ErrorCode == 429)
                {
                    var retryAfterSeconds = ex.Parameters?.RetryAfter ?? delaySeconds;
                    logger.LogWarning(ex, "Rate limit hit (429) syncing chain {ChainId}. Retrying after {Seconds} seconds.", chainId, retryAfterSeconds);

                    if (attempt == maxRetries)
                    {
                        chain.TelegramSyncStatus = TelegramSyncStatus.Failed;
                        chain.LastSyncError = ex.Message;
                        chain.LastSyncedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                        throw;
                    }

                    await Task.Delay(retryAfterSeconds * 1000, cancellationToken);
                    delaySeconds *= 2;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error syncing chain {ChainId} message (Attempt {Attempt}/{MaxRetries}): {Error}", chainId, attempt, maxRetries, ex.Message);

                    if (attempt == maxRetries)
                    {
                        chain.TelegramSyncStatus = TelegramSyncStatus.Failed;
                        chain.LastSyncError = ex.Message;
                        chain.LastSyncedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                        throw;
                    }

                    await Task.Delay(delaySeconds * 1000, cancellationToken);
                    delaySeconds *= 2;
                }
            }
        }, cancellationToken);
    }

    public virtual async Task<(long ChatId, long MessageId)> SendChainMessageAsync(
        long chatId,
        string publicId,
        string text,
        CancellationToken cancellationToken)
    {
        var message = await botClient.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.Html,
            replyMarkup: await BuildReplyMarkupAsync(chatId, publicId, cancellationToken),
            cancellationToken: cancellationToken);

        return (message.Chat.Id, message.MessageId);
    }

    public virtual async Task EditChainMessageAsync(
        long chatId,
        long messageId,
        string publicId,
        string text,
        CancellationToken cancellationToken)
    {
        await syncService.ExecuteLockedAsync(chatId, messageId, async () =>
        {
            try
            {
                await botClient.EditMessageText(
                    chatId: chatId,
                    messageId: (int)messageId,
                    text: text,
                    parseMode: ParseMode.Html,
                    replyMarkup: await BuildReplyMarkupAsync(chatId, publicId, cancellationToken),
                    cancellationToken: cancellationToken);
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.Message.Contains("message is not modified"))
            {
                // Ignore if message is identical
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex)
            {
                logger.LogWarning(ex, "Telegram API error editing message {MessageId} in chat {ChatId}: {Error}", messageId, chatId, ex.Message);
            }
        }, cancellationToken);
    }

    public virtual async Task LeaveChatAsync(long chatId, CancellationToken cancellationToken)
    {
        await botClient.LeaveChat(chatId, cancellationToken);
    }

    public virtual async Task SendOpenChainWebAppAsync(
        long chatId,
        string publicId,
        string title,
        CancellationToken cancellationToken)
    {
        var webAppUrl = BuildWebAppUrl(publicId);
        var replyMarkup = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithWebApp("填写名字并加入", new WebAppInfo { Url = webAppUrl })
            }
        });

        await botClient.SendMessage(
            chatId: chatId,
            text: $"请填写你要显示在接龙里的名字，然后加入“{title}”。",
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }

    public virtual async Task SendTextMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        await botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);
    }

    public virtual async Task AnswerCallbackAsync(string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        await botClient.AnswerCallbackQuery(
            callbackQueryId,
            text,
            showAlert: false,
            cancellationToken: cancellationToken);
    }

    public virtual async Task EnsureWebhookAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookBaseUrl))
        {
            return;
        }

        var url = $"{_options.WebhookBaseUrl.TrimEnd('/')}{_options.WebhookPath}";
        await botClient.SetWebhook(
            url: url,
            secretToken: _options.WebhookSecret,
            allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
            cancellationToken: cancellationToken);
    }

    private string BuildWebAppUrl(string publicId)
    {
        var baseUrl = _options.WebhookBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return $"https://example.com/webapp/index.html?chain_id={publicId}";
        }

        // Force HTTPS for Telegram WebApp
        if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "https://" + baseUrl[7..];
        }
        else if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "https://" + baseUrl;
        }

        var url = $"{baseUrl.TrimEnd('/')}/webapp/index.html?chain_id={publicId}";
        logger.LogInformation("Generated WebApp URL: {Url}", url);
        return url;
    }

    private async Task<InlineKeyboardMarkup> BuildReplyMarkupAsync(long chatId, string publicId, CancellationToken cancellationToken)
    {
        var webAppUrl = BuildWebAppUrl(publicId);

        if (chatId > 0)
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithWebApp("填写名字并加入", new WebAppInfo { Url = webAppUrl })
                }
            });
        }

        var botUsername = await GetBotUsernameAsync(cancellationToken);
        var joinUrl = $"https://t.me/{botUsername}?start=join_{publicId}";
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("参加", $"join:{publicId}"),
                InlineKeyboardButton.WithCallbackData("退出", $"leave:{publicId}")
            },
            new[]
            {
                InlineKeyboardButton.WithUrl("填写自定义名字", joinUrl),
                InlineKeyboardButton.WithCallbackData("关闭接龙", $"close:{publicId}")
            }
        });
    }

    private async Task<string> GetBotUsernameAsync(CancellationToken cancellationToken)
    {
        var me = await botClient.GetMe(cancellationToken);
        if (string.IsNullOrWhiteSpace(me.Username))
        {
            throw new InvalidOperationException("Bot username is required to generate join links.");
        }

        return me.Username;
    }
}
