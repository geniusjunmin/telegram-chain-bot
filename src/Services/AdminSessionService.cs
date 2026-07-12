using System;
using System.Collections.Concurrent;

namespace TelegramChainBot.Services;

public sealed class AdminSessionService
{
    private readonly ConcurrentDictionary<string, int> _activeSessions = new();

    public string RegisterSession(int adminId)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        _activeSessions[sessionId] = adminId;
        return sessionId;
    }

    public bool IsSessionActive(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        return _activeSessions.ContainsKey(sessionId);
    }

    public int? GetAdminId(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        return _activeSessions.TryGetValue(sessionId, out var adminId) ? adminId : null;
    }

    public void RevokeSession(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _activeSessions.TryRemove(sessionId, out _);
    }

    public void RevokeAllSessionsForAdmin(int adminId)
    {
        foreach (var pair in _activeSessions)
        {
            if (pair.Value == adminId)
            {
                _activeSessions.TryRemove(pair.Key, out _);
            }
        }
    }
}
