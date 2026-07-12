using System;

namespace TelegramChainBot.Database.Models;

public sealed class ManagedChat
{
    public long ChatId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ManagedChatStatus Status { get; set; } = ManagedChatStatus.Disabled;
    public DateTimeOffset CreatedAt { get; set; }
    public string AuthorizedBy { get; set; } = string.Empty;
}
