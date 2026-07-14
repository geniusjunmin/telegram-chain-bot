using System;

namespace TelegramChainBot.Database.Models;

public sealed class ChainMember
{
    public long Id { get; set; }
    public long ChainId { get; set; }
    public long TelegramUserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? TelegramUsername { get; set; }
    public string? TelegramFullName { get; set; }
    public ChainMemberStatus Status { get; set; } = ChainMemberStatus.Active;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeftAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}
