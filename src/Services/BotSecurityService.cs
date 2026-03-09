using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TelegramChainBot.Options;

namespace TelegramChainBot.Services;

public sealed class BotSecurityService(IOptions<BotOptions> options)
{
    private readonly BotOptions _options = options.Value;

    public bool ValidateInitData(string? initData)
    {
        if (string.IsNullOrWhiteSpace(initData))
        {
            return false;
        }

        var parsed = QueryHelpers.ParseQuery(initData)
            .ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.Ordinal);

        if (!parsed.Remove("hash", out var hash) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        var checkString = string.Join("\n", parsed
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Value}"));

        using var hmacForKey = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secret = hmacForKey.ComputeHash(Encoding.UTF8.GetBytes(_options.BotToken));

        using var hmac = new HMACSHA256(secret);
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(checkString))).ToLowerInvariant();

        return string.Equals(computed, hash, StringComparison.OrdinalIgnoreCase);
    }
}
