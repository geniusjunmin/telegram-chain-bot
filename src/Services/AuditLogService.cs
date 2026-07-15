using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Services;

public class AuditLogService(AppDbContext db, IHttpContextAccessor httpContextAccessor)
{
    public virtual async Task LogAsync(
        string action,
        string entityType,
        string entityId,
        long? chatId = null,
        string beforeJson = "",
        string afterJson = "",
        bool success = true,
        string failureReason = "",
        string actorType = "Admin",
        int? overrideActorAdminId = null,
        long? actorTelegramUserId = null)
    {
        var context = httpContextAccessor.HttpContext;

        string ipAddress = string.Empty;
        string userAgent = string.Empty;
        string correlationId = string.Empty;
        int? actorAdminId = overrideActorAdminId;

        if (context != null)
        {
            ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            userAgent = context.Request.Headers.UserAgent.ToString();
            correlationId = context.TraceIdentifier;

            if (actorAdminId == null && context.User.Identity?.IsAuthenticated == true)
            {
                var nameIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(nameIdClaim, out var parsedId))
                {
                    actorAdminId = parsedId;
                }
            }
        }

        var log = new AuditLog
        {
            ActorType = actorType,
            ActorAdminId = actorAdminId,
            ActorTelegramUserId = actorTelegramUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ChatId = chatId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            IpAddressHash = HashIpAddress(ipAddress),
            UserAgent = userAgent,
            CreatedAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
            Success = success,
            FailureReason = failureReason
        };

        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();
    }

    private static string HashIpAddress(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return string.Empty;
        var keyBytes = Encoding.UTF8.GetBytes("TelegramChainBotAuditLogIPSecretKey");
        var ipBytes = Encoding.UTF8.GetBytes(ip);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(ipBytes);
        var hex = Convert.ToHexString(hash);
        return hex[..Math.Min(8, hex.Length)];
    }
}
