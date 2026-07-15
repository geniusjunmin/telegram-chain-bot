using System;

namespace TelegramChainBot.Services;

public sealed class BackgroundWorkerTracker
{
    public DateTimeOffset LastExpirationCheck { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastBackupCheck { get; set; } = DateTimeOffset.UtcNow;
}
