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
        return ValidateAndExtractUser(initData) != null;
    }

    public TelegramUser? ValidateAndExtractUser(string? initData)
    {
        if (string.IsNullOrWhiteSpace(initData))
        {
            return null;
        }

        var parsed = QueryHelpers.ParseQuery(initData)
            .ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.Ordinal);

        if (!parsed.Remove("hash", out var hash) || string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        var checkString = string.Join("\n", parsed
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Value}"));

        using var hmacForKey = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secret = hmacForKey.ComputeHash(Encoding.UTF8.GetBytes(_options.BotToken));

        using var hmac = new HMACSHA256(secret);
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(checkString))).ToLowerInvariant();

        if (!string.Equals(computed, hash, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (parsed.TryGetValue("user", out var userJson))
        {
            try
            {
                var user = System.Text.Json.JsonSerializer.Deserialize<TelegramUser>(userJson, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return user;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}

public sealed record TelegramUser(
    long Id,
    string? Username,
    [property: System.Text.Json.Serialization.JsonPropertyName("first_name")] string FirstName,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_name")] string? LastName);
