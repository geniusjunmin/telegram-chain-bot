using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TelegramChainBot.Api;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;
using Xunit;

namespace TelegramChainBot.IntegrationTests;

public class ChainApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ChainApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetChain_ReturnsNotFound_WhenChainDoesNotExist()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/chains/99999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task JoinChain_ReturnsUnauthorized_WhenInitDataIsInvalid()
    {
        // Arrange
        var client = _factory.CreateClient();
        var requestBody = new ChainController.JoinRequest("TestName");

        // Act
        var response = await client.PostAsJsonAsync("/api/chains/non-existent-public-id/join", requestBody);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task JoinChain_Succeeds_WhenInitDataIsValidAndChainExists()
    {
        // Arrange
        var client = _factory.CreateClient();

        // 1. Create a chain in the DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var chain = new Chain
        {
            PublicId = Guid.NewGuid().ToString("N"),
            Title = "Dinner Join Test",
            CreatorTelegramUserId = 12345,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Chains.Add(chain);
        await db.SaveChangesAsync();

        // 2. Generate valid init data
        var userJson = "{\"id\":12345,\"first_name\":\"Test\",\"username\":\"testuser\"}";
        var authDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var botToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11"; // matches CustomWebApplicationFactory BOT_TOKEN
        var initData = GenerateValidInitData(botToken, userJson, authDate);

        var requestBody = new ChainController.JoinRequest("Alice");
        
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/chains/{chain.PublicId}/join")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add("X-Telegram-Init-Data", initData);

        // Act
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Failed: {response.StatusCode}. Body: {body}");
        var result = await response.Content.ReadFromJsonAsync<JoinResponse>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.Joined);
        Assert.Single(result.UpdatedMembers);
        Assert.Equal("Alice", result.UpdatedMembers[0].DisplayName);
    }

    [Fact]
    public async Task JoinChain_RejectsExpiredInitData()
    {
        // Arrange
        var client = _factory.CreateClient();

        // 1. Create a chain in the DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var chain = new Chain
        {
            PublicId = Guid.NewGuid().ToString("N"),
            Title = "Dinner Join Test",
            CreatorTelegramUserId = 12345,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Chains.Add(chain);
        await db.SaveChangesAsync();

        // 2. Generate expired init data (older than 5 minutes)
        var userJson = "{\"id\":12345,\"first_name\":\"Test\",\"username\":\"testuser\"}";
        var authDate = DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds();
        var botToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";
        var initData = GenerateValidInitData(botToken, userJson, authDate);

        var requestBody = new ChainController.JoinRequest("Alice");
        
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/chains/{chain.PublicId}/join")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add("X-Telegram-Init-Data", initData);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_ReturnsUnauthorized_WhenSecretTokenHeaderIsInvalid()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/telegram/webhook")
        {
            Content = JsonContent.Create(new { update_id = 1234 })
        };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "wrong_secret");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_ReturnsBadRequest_WhenSecretIsCorrectButBodyIsEmpty()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/telegram/webhook")
        {
            Content = new StringContent("", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "super_secret_webhook_token_32_bytes");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private string GenerateValidInitData(string botToken, string userJson, long authDate)
    {
        var parameters = new Dictionary<string, string>
        {
            { "user", userJson },
            { "auth_date", authDate.ToString() }
        };

        var checkString = string.Join("\n", parameters
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Value}"));

        using var hmacForKey = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secret = hmacForKey.ComputeHash(Encoding.UTF8.GetBytes(botToken));

        using var hmac = new HMACSHA256(secret);
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(checkString))).ToLowerInvariant();

        return $"user={Uri.EscapeDataString(userJson)}&auth_date={authDate}&hash={hash}";
    }

    private class JoinResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public bool Success { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("joined")]
        public bool Joined { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("updated_members")]
        public List<MemberDto> UpdatedMembers { get; set; } = new();
    }

    private class MemberDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
    }
}
