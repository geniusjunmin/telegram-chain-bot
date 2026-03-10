using System.Collections.Concurrent;

namespace TelegramChainBot.Services;

public sealed class AdminSessionService
{
    private readonly ConcurrentDictionary<string, int> _sessions = new();

    public string CreateSession(int adminId)
    {
        var token = Guid.NewGuid().ToString("N");
        _sessions[token] = adminId;
        return token;
    }

    public int? GetAdminId(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        return _sessions.TryGetValue(token, out var adminId) ? adminId : null;
    }

    public void RemoveSession(string token)
    {
        _sessions.TryRemove(token, out _);
    }
}
