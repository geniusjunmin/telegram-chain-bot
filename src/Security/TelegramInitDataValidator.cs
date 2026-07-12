using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TelegramChainBot.Options;

namespace TelegramChainBot.Security;

public sealed record ValidatedTelegramUser(
    long UserId,
    string? Username,
    string FirstName,
    string? LastName,
    DateTimeOffset AuthenticatedAt);

public sealed class TelegramInitDataValidator(IOptions<BotOptions> options)
{
    private readonly BotOptions _options = options.Value;

    public ValidatedTelegramUser? Validate(string? initData)
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

        // Validate HMAC signature
        using var hmacForKey = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secret = hmacForKey.ComputeHash(Encoding.UTF8.GetBytes(_options.BotToken));

        using var hmac = new HMACSHA256(secret);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(checkString));

        byte[] inputHashBytes;
        try
        {
            inputHashBytes = Convert.FromHexString(hash);
        }
        catch
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(computedHash, inputHashBytes))
        {
            return null;
        }

        // Validate auth_date
        if (!parsed.TryGetValue("auth_date", out var authDateStr) || !long.TryParse(authDateStr, out var authDateUnix))
        {
            return null;
        }

        var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnix);
        var now = DateTimeOffset.UtcNow;
        var age = now - authDate;

        // Reject if data is older than 5 minutes or more than 5 seconds in the future
        if (age.TotalSeconds > 300 || age.TotalSeconds < -5)
        {
            return null;
        }

        // Parse user data
        if (!parsed.TryGetValue("user", out var userJson) || string.IsNullOrWhiteSpace(userJson))
        {
            return null;
        }

        try
        {
            var telegramUser = JsonSerializer.Deserialize<TelegramUserPayload>(userJson);
            if (telegramUser == null || telegramUser.Id == 0)
            {
                return null;
            }

            return new ValidatedTelegramUser(
                telegramUser.Id,
                telegramUser.Username,
                telegramUser.FirstName ?? string.Empty,
                telegramUser.LastName,
                authDate
            );
        }
        catch
        {
            return null;
        }
    }

    private sealed class TelegramUserPayload
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }
    }
}
