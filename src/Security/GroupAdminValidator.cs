using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace TelegramChainBot.Security;

public sealed class GroupAdminValidator(ITelegramBotClient botClient)
{
    private readonly ConcurrentDictionary<string, (bool IsAdmin, DateTime Expiry)> _cache = new();

    public async Task<bool> IsAdminOrOwnerAsync(long chatId, long userId, CancellationToken cancellationToken = default)
    {
        var key = $"{chatId}:{userId}";
        if (_cache.TryGetValue(key, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            return cached.IsAdmin;
        }

        try
        {
            var member = await botClient.GetChatMember(chatId, userId, cancellationToken);
            var isAdmin = member.Status == ChatMemberStatus.Creator || member.Status == ChatMemberStatus.Administrator;

            // Cache for 3 minutes
            _cache[key] = (isAdmin, DateTime.UtcNow.AddMinutes(3));
            return isAdmin;
        }
        catch
        {
            return false;
        }
    }
}
