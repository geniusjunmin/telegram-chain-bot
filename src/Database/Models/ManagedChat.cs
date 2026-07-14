using System;

namespace TelegramChainBot.Database.Models;

public sealed class ManagedChat
{
    public long ChatId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ChatType { get; set; } = string.Empty;
    public AuthorizationStatus AuthorizationStatus { get; set; } = AuthorizationStatus.Pending;
    public CreatePolicy CreatePolicy { get; set; } = CreatePolicy.Everyone;
    public bool IsJoinEnabled { get; set; } = true;
    public int DefaultMaxMembers { get; set; } = 100;
    public int MaxActiveChains { get; set; } = 5;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ApprovedAt { get; set; }
    public int? ApprovedByAdminId { get; set; }
    public DateTimeOffset? BlockedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
