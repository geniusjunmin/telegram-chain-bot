using System;

namespace TelegramChainBot.Database.Models;

public sealed class AdminAccount
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public AdminRole Role { get; set; } = AdminRole.AuditorAdmin;
    public DateTimeOffset? LockoutEnd { get; set; }
    public int AccessFailedCount { get; set; }
    public bool IsDisabled { get; set; }
}
