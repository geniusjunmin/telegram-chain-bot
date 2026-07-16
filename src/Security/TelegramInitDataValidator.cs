using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using TelegramChainBot.Database;
using TelegramChainBot.Options;
using TelegramChainBot.Services;

namespace TelegramChainBot.Security;

public sealed record ValidatedTelegramUser(
    long UserId,
    string? Username,
    string FirstName,
    string? LastName,
    DateTimeOffset AuthenticatedAt);

public sealed class TelegramInitDataValidator(
    AppDbContext db,
    BotTokenProvider tokenProvider,
    ILogger<TelegramInitDataValidator> logger)
{
    public ValidatedTelegramUser? Validate(string? initData)
    {
        if (string.IsNullOrWhiteSpace(initData))
        {
            logger.LogWarning("Telegram InitData validation failed: initData is null or empty.");
            return null;
        }

        var parsed = QueryHelpers.ParseQuery(initData)
            .ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.Ordinal);

        if (!parsed.Remove("hash", out var hash) || string.IsNullOrWhiteSpace(hash))
        {
            logger.LogWarning("Telegram InitData validation failed: missing or empty hash.");
            return null;
        }

        var checkString = string.Join("\n", parsed
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Value}"));

        // Validate HMAC signature using current active Bot Token
        var botToken = tokenProvider.Token ?? string.Empty;
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning("Telegram InitData validation failed: active Bot Token is empty.");
            return null;
        }

        using var hmacForKey = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secret = hmacForKey.ComputeHash(Encoding.UTF8.GetBytes(botToken));

        using var hmac = new HMACSHA256(secret);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(checkString));

        byte[] inputHashBytes;
        try
        {
            inputHashBytes = Convert.FromHexString(hash);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram InitData validation failed: hash is not valid hex.");
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(computedHash, inputHashBytes))
        {
            logger.LogWarning("Telegram InitData validation failed: HMAC signature mismatch. Please check if the Bot Token configured in the database settings is correct.");
            return null;
        }

        // Validate auth_date
        if (!parsed.TryGetValue("auth_date", out var authDateStr) || !long.TryParse(authDateStr, out var authDateUnix))
        {
            logger.LogWarning("Telegram InitData validation failed: missing or invalid auth_date.");
            return null;
        }

        var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnix);
        var now = DateTimeOffset.UtcNow;
        var age = now - authDate;

        var settings = db.SystemSettings.FirstOrDefault();
        var maxAge = settings?.TelegramInitDataMaxAgeSeconds ?? 86400;

        // Reject if data is older than maxAge or more than 60 seconds in the future
        if (age.TotalSeconds > maxAge || age.TotalSeconds < -60)
        {
            logger.LogWarning("Telegram InitData validation failed: credentials expired or invalid future offset. Age: {AgeSeconds}s, MaxAge: {MaxAgeSeconds}s.", age.TotalSeconds, maxAge);
            return null;
        }

        // Parse user data
        if (!parsed.TryGetValue("user", out var userJson) || string.IsNullOrWhiteSpace(userJson))
        {
            logger.LogWarning("Telegram InitData validation failed: missing or empty user payload.");
            return null;
        }

        try
        {
            var telegramUser = JsonSerializer.Deserialize<TelegramUserPayload>(userJson);
            if (telegramUser == null || telegramUser.Id == 0)
            {
                logger.LogWarning("Telegram InitData validation failed: deserialized user payload has ID 0.");
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram InitData validation failed: user payload deserialization failed. Raw json: {RawJson}", userJson);
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
