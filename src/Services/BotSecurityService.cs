using Microsoft.Extensions.Options;
using TelegramChainBot.Options;
using TelegramChainBot.Security;

namespace TelegramChainBot.Services;

public sealed class BotSecurityService(TelegramInitDataValidator validator)
{
    public bool ValidateInitData(string? initData)
    {
        return validator.Validate(initData) != null;
    }
}
