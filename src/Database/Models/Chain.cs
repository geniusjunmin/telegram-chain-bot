using System;

namespace TelegramChainBot.Database.Models;

public sealed class Chain
{
    public long Id { get; set; }
    public string PublicId { get; set; } = string.Empty;
    public long ChatId { get; set; }
    public long CreatorTelegramUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ChainStatus Status { get; set; } = ChainStatus.Active;
    public long? MessageId { get; set; }
    public int MaxMembers { get; set; } = 100;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }
    public long? ClosedByTelegramUserId { get; set; }
    public int? ClosedByAdminId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int? DeletedByAdminId { get; set; }
    public TelegramSyncStatus TelegramSyncStatus { get; set; } = TelegramSyncStatus.Pending;
    public string? LastSyncError { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public int Version { get; set; } = 1;
}
