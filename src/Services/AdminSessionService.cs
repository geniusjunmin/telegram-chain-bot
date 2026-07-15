using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Services;

public sealed class AdminSessionService(AppDbContext db)
{
    public async Task<string> RegisterSessionAsync(int adminId, TimeSpan expiry, CancellationToken ct)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new AdminSession
        {
            SessionId = sessionId,
            AdminId = adminId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(expiry),
            IsRevoked = false
        };
        db.AdminSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return sessionId;
    }

    public async Task<bool> IsSessionActiveAsync(string? sessionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        var session = await db.AdminSessions.FindAsync([sessionId], ct);
        if (session == null || session.IsRevoked || session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }
        return true;
    }

    public async Task<int?> GetAdminIdAsync(string? sessionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        var session = await db.AdminSessions.FindAsync([sessionId], ct);
        if (session == null || session.IsRevoked || session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }
        return session.AdminId;
    }

    public async Task RevokeSessionAsync(string? sessionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        var session = await db.AdminSessions.FindAsync([sessionId], ct);
        if (session != null)
        {
            session.IsRevoked = true;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RevokeAllSessionsForAdminAsync(int adminId, CancellationToken ct)
    {
        var sessions = await db.AdminSessions
            .Where(s => s.AdminId == adminId && !s.IsRevoked)
            .ToListAsync(ct);
        foreach (var s in sessions)
        {
            s.IsRevoked = true;
        }
        await db.SaveChangesAsync(ct);
    }
}
