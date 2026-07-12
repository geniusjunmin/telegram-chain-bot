namespace TelegramChainBot.Database.Models;

public sealed class Chain
{
    public long Id { get; set; }
    public string PublicId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long CreatorId { get; set; }
    public long MessageId { get; set; }
    public long ChatId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
