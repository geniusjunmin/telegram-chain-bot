using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramChainBot.Options;

namespace TelegramChainBot.Services;

public class TelegramService(
    ITelegramBotClient botClient, 
    IOptions<BotOptions> options, 
    TelegramMessageSyncService syncService,
    ILogger<TelegramService> logger)
{
    private readonly BotOptions _options = options.Value;

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
                InlineKeyboardButton.WithUrl("DIY 名字参加", joinUrl)
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
