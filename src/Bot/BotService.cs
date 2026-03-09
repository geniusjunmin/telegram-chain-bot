using Telegram.Bot.Types;
using TelegramChainBot.Services;

namespace TelegramChainBot.Bot;

public sealed class BotService(UpdateHandler updateHandler)
{
    public async Task HandleWebhookAsync(Update update, CancellationToken cancellationToken)
    {
        await updateHandler.HandleAsync(update, cancellationToken);
    }
}
