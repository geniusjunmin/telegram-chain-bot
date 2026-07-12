using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace TelegramChainBot.Security;

public sealed class WebhookSecretValidator(IConfiguration configuration)
{
    private readonly string? _configuredSecret = configuration["TELEGRAM_WEBHOOK_SECRET"];

    public bool Validate(string? requestSecret)
    {
        if (string.IsNullOrEmpty(requestSecret) || string.IsNullOrEmpty(_configuredSecret))
        {
            return false;
        }

        byte[] requestHash = SHA256.HashData(Encoding.UTF8.GetBytes(requestSecret));
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(_configuredSecret));

        return CryptographicOperations.FixedTimeEquals(requestHash, expectedHash);
    }
}
