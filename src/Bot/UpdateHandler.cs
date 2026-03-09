using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramChainBot.Services;

namespace TelegramChainBot.Bot;

public sealed class UpdateHandler(ChainService chainService, TelegramService telegramService)
{
    public async Task HandleAsync(Update update, CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.Message && update.Message?.Text is not null)
        {
            await HandleMessageAsync(update.Message, cancellationToken);
            return;
        }

        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is not null)
        {
            await HandleCallbackAsync(update.CallbackQuery, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        var text = message.Text!.Trim();
        if (!text.StartsWith("/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Split by space to get the command
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var commandPart = parts[0];
        
        // Handle /command@botname
        var actualCommand = commandPart.Split('@')[0];

        // Check if it's our command
        if (!actualCommand.Equals("/start_chain", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var title = parts.Length > 1 ? parts[1].Trim() : "聚餐接龙";
        if (string.IsNullOrWhiteSpace(title))
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
        if (string.IsNullOrWhiteSpace(callback.Data) || !callback.Data.StartsWith("join:", StringComparison.OrdinalIgnoreCase))
        {
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
        
        // Answer early to stop the loading spinner
        await telegramService.AnswerCallbackAsync(callback.Id, added ? "加入成功" : "你已经参加过了", cancellationToken);

        if (added)
        {
            var messageText = ChainService.FormatChainMessage(chain.Title, members);
            await telegramService.EditChainMessageAsync(chain.ChatId, chain.MessageId, chainId, messageText, cancellationToken);
        }
    }
}
