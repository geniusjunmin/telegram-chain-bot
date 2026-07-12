namespace TelegramChainBot.Database.Models;

public sealed class ProcessedTelegramUpdate
{
    public int UpdateId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
