namespace TelegramChainBot.Options;

public sealed class BotOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string? WebhookBaseUrl { get; set; }
    public string WebhookPath { get; set; } = "/telegram/webhook";
    public string? WebhookSecret { get; set; }
}
