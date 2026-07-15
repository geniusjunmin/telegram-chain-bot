using System;

namespace TelegramChainBot.Database.Models;

public sealed class SystemSetting
{
    public int Id { get; set; } = 1;
    public WhitelistMode WhitelistMode { get; set; } = WhitelistMode.Enforced;
    public string UnauthorizedChatBehavior { get; set; } = "WarnAndLeave";
    public CreatePolicy DefaultCreatePolicy { get; set; } = CreatePolicy.Everyone;
    public int DefaultMaxMembers { get; set; } = 100;
    public int DefaultChainExpiryHours { get; set; } = 24;
    public int MaxActiveChainsPerChat { get; set; } = 5;
    public int TelegramInitDataMaxAgeSeconds { get; set; } = 86400;
    public int DeletedDataRetentionDays { get; set; } = 30;
    public bool RequireMfaForSuperAdmin { get; set; } = false;
    public string? BotToken { get; set; }
    public string StaticVersion { get; set; } = "1.0.0";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? UpdatedByAdminId { get; set; }
}
