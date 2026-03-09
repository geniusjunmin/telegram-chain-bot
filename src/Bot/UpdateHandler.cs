using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramChainBot.Services;

namespace TelegramChainBot.Bot;

public sealed class UpdateHandler(ChainService chainService, TelegramService telegramService, ILogger<UpdateHandler> logger)
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

        if (TryParseStartChainCommand(message, out var title))
        {
            await CreateChainAsync(message, title, cancellationToken);
            return;
        }

        if (TryParseJoinChainCommand(message, out var chainId))
        {
            await SendJoinWebAppAsync(message, chainId, cancellationToken);
            return;
        }
    }

    private async Task CreateChainAsync(Message message, string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title) || title.StartsWith("@"))
        {
            title = "聚餐接龙";
        }

        var creatorId = message.From?.Id ?? 0;
        logger.LogInformation("Creating chain with Title: {Title}, Creator: {CreatorId}", title, creatorId);

        var chainId = await chainService.CreateChainAsync(title, creatorId, cancellationToken);

        try
        {
            var output = ChainService.FormatChainMessage(title, []);
            var (chatId, messageId) = await telegramService.SendChainMessageAsync(message.Chat.Id, chainId, output, cancellationToken);
            await chainService.SetMessageInfoAsync(chainId, chatId, messageId, cancellationToken);
        }
        catch
        {
            await chainService.DeleteChainAsync(chainId, cancellationToken);
            throw;
        }
    }

    private async Task SendJoinWebAppAsync(Message message, long chainId, CancellationToken cancellationToken)
    {
        if (message.Chat.Type is not ChatType.Private)
        {
            return;
        }

        var chain = await chainService.GetChainAsync(chainId, cancellationToken);
        if (chain is null)
        {
            await telegramService.SendTextMessageAsync(message.Chat.Id, "这个接龙不存在或已失效。", cancellationToken);
            return;
        }

        await telegramService.SendOpenChainWebAppAsync(message.Chat.Id, chainId, chain.Title, cancellationToken);
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

    private static bool TryParseJoinChainCommand(Message message, out long chainId)
    {
        chainId = 0;
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

        return long.TryParse(payload["join_".Length..], out chainId);
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
        catch (Exception)
        {
            // Try to answer anyway if it hasn't been answered
            try { await telegramService.AnswerCallbackAsync(callback.Id, "出错了，请稍后再试", cancellationToken); } catch {}
        }
    }
}
