namespace TelegramChainBot.Database.Models;

public sealed class ChainMember
{
    public long Id { get; set; }
    public long ChainId { get; set; }
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTimeOffset JoinTime { get; set; }
}
