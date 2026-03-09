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
        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("参加接龙", $"join:{chainId}"),
                InlineKeyboardButton.WithWebApp("打开 WebApp", new WebAppInfo(BuildWebAppUrl(chainId)))
            }
        });

        var message = await botClient.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.Html,
            replyMarkup: inlineKeyboard,
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
        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("参加接龙", $"join:{chainId}"),
                InlineKeyboardButton.WithWebApp("打开 WebApp", new WebAppInfo(BuildWebAppUrl(chainId)))
            }
        });

        try
        {
            await botClient.EditMessageText(
                chatId: chatId,
                messageId: (int)messageId,
                text: text,
                parseMode: ParseMode.Html,
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);
        }
        catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.Message.Contains("message is not modified"))
        {
            // Ignore if message is identical
        }
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
}
