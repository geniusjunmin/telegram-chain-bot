using System;

namespace TelegramChainBot.Database.Models;

public sealed class AuditLog
{
    public long Id { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public int? ActorAdminId { get; set; }
    public long? ActorTelegramUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public long? ChatId { get; set; }
    public string BeforeJson { get; set; } = string.Empty;
    public string AfterJson { get; set; } = string.Empty;
    public string IpAddressHash { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string FailureReason { get; set; } = string.Empty;
}
