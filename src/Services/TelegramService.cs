using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramChainBot.Options;

namespace TelegramChainBot.Services;

public sealed class TelegramService(ITelegramBotClient botClient, IOptions<BotOptions> options, ILogger<TelegramService> logger)
{
    private readonly BotOptions _options = options.Value;

    public async Task<(long ChatId, long MessageId)> SendChainMessageAsync(
        long chatId,
        long chainId,
        string text,
        CancellationToken cancellationToken)
    {
        var message = await botClient.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.Html,
            replyMarkup: await BuildReplyMarkupAsync(chatId, chainId, cancellationToken),
            cancellationToken: cancellationToken);

        return (message.Chat.Id, message.MessageId);
    }

    public async Task EditChainMessageAsync(
        long chatId,
        long messageId,
        long chainId,
        string text,
        CancellationToken cancellationToken)
    {
        try
        {
            await botClient.EditMessageText(
                chatId: chatId,
                messageId: (int)messageId,
                text: text,
                parseMode: ParseMode.Html,
                replyMarkup: await BuildReplyMarkupAsync(chatId, chainId, cancellationToken),
                cancellationToken: cancellationToken);
        }
        catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.Message.Contains("message is not modified"))
        {
            // Ignore if message is identical
        }
    }

    public async Task SendOpenChainWebAppAsync(
        long chatId,
        long chainId,
        string title,
        CancellationToken cancellationToken)
    {
        var webAppUrl = BuildWebAppUrl(chainId);
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

    public async Task SendTextMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        await botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);
    }

    public async Task AnswerCallbackAsync(string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        await botClient.AnswerCallbackQuery(
            callbackQueryId,
            text,
            showAlert: false,
            cancellationToken: cancellationToken);
    }

    public async Task EnsureWebhookAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookBaseUrl))
        {
            return;
        }

        var url = $"{_options.WebhookBaseUrl.TrimEnd('/')}{_options.WebhookPath}/{_options.BotToken}";
        await botClient.SetWebhook(url, cancellationToken: cancellationToken);
    }

    private string BuildWebAppUrl(long chainId)
    {
        var baseUrl = _options.WebhookBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return $"https://example.com/webapp/index.html?chain_id={chainId}";
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

        var url = $"{baseUrl.TrimEnd('/')}/webapp/index.html?chain_id={chainId}";
        logger.LogInformation("Generated WebApp URL: {Url}", url);
        return url;
    }

    private async Task<InlineKeyboardMarkup> BuildReplyMarkupAsync(long chatId, long chainId, CancellationToken cancellationToken)
    {
        var webAppUrl = BuildWebAppUrl(chainId);

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
        var joinUrl = $"https://t.me/{botUsername}?start=join_{chainId}";
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithUrl("私聊填写名字", joinUrl)
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
