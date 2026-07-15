using System;

namespace TelegramChainBot.Database.Models;

public class AdminSession
{
    public string SessionId { get; set; } = string.Empty;
    public int AdminId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
}
