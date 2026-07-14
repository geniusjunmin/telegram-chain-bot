namespace TelegramChainBot.Database.Models;

public enum CreatePolicy
{
    Everyone = 1,
    ChatAdministrators = 2,
    BotOwners = 3,
    Disabled = 4
}
