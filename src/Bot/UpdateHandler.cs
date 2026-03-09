using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramChainBot.Services;

namespace TelegramChainBot.Bot;

public sealed class UpdateHandler(ChainService chainService, TelegramService telegramService, ILogger<UpdateHandler> logger)
{
    public async Task HandleAsync(Update update, CancellationToken cancellationToken)
    {
        logger.LogInformation("Received update type: {Type}, ID: {Id}", update.Type, update.Id);

        if (update.Type == UpdateType.Message && update.Message?.Text is not null)
        {
            await HandleMessageAsync(update.Message, cancellationToken);
            return;
        }

        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is not null)
        {
            logger.LogInformation("Handling CallbackQuery from User: {UserId}, Data: {Data}", 
                update.CallbackQuery.From.Id, update.CallbackQuery.Data);
            await HandleCallbackAsync(update.CallbackQuery, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        var text = message.Text!.Trim();
        
        // Handle "@bot /command" or "/command"
        string? title = null;
        if (text.Contains("/start_chain", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(new[] { "/start_chain" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                // If it was "@bot /start_chain something", parts might contain ["@bot ", " something"]
                // We want the part after /start_chain
                title = parts[^1].Trim();
            }
        }
        else
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(title) || title.StartsWith("@"))
        {
            title = "聚餐接龙";
        }

        var creatorId = message.From?.Id ?? 0;
        var chainId = await chainService.CreateChainAsync(title, creatorId, cancellationToken);
        var output = ChainService.FormatChainMessage(title, []);
        
        var (chatId, messageId) = await telegramService.SendChainMessageAsync(message.Chat.Id, chainId, output, cancellationToken);
        await chainService.SetMessageInfoAsync(chainId, chatId, messageId, cancellationToken);
    }

    private async Task HandleCallbackAsync(CallbackQuery callback, CancellationToken cancellationToken)
    {
        // Always answer early to stop the spinner
        try 
        {
            if (string.IsNullOrWhiteSpace(callback.Data) || !callback.Data.StartsWith("join:", StringComparison.OrdinalIgnoreCase))
            {
                await telegramService.AnswerCallbackAsync(callback.Id, "无效的操作", cancellationToken);
                return;
            }

            if (!long.TryParse(callback.Data.Split(':', 2)[1], out var chainId))
            {
                await telegramService.AnswerCallbackAsync(callback.Id, "无效的接龙编号", cancellationToken);
                return;
            }

            var chain = await chainService.GetChainAsync(chainId, cancellationToken);
            if (chain is null)
            {
                await telegramService.AnswerCallbackAsync(callback.Id, "接龙不存在", cancellationToken);
                return;
            }

            var user = callback.From;
            var username = !string.IsNullOrWhiteSpace(user.Username)
                ? user.Username
                : user.FirstName;

            var (added, members) = await chainService.JoinAsync(chainId, user.Id, username, cancellationToken);
            
            await telegramService.AnswerCallbackAsync(callback.Id, added ? "加入成功" : "你已经参加过了", cancellationToken);

            if (added)
            {
                var messageText = ChainService.FormatChainMessage(chain.Title, members);
                await telegramService.EditChainMessageAsync(chain.ChatId, chain.MessageId, chainId, messageText, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Try to answer anyway if it hasn't been answered
            try { await telegramService.AnswerCallbackAsync(callback.Id, "出错了，请稍后再试", cancellationToken); } catch {}
        }
    }
}
