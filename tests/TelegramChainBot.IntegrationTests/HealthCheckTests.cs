using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TelegramChainBot.IntegrationTests;

public class HealthCheckTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HealthCheckTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthLiveEndpoint_ReturnsHealthy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(content);
        Assert.Equal("Healthy", content.Status);
    }

    [Fact]
    public async Task HealthReadyEndpoint_ReturnsHealthy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<HealthReadyResponse>();
        Assert.NotNull(content);
        Assert.Equal("Healthy", content.Status);
        Assert.Equal("Connected", content.Database);
        Assert.Equal("Applied", content.Migrations);
        Assert.Equal("Valid", content.Config);
        Assert.Equal("Writable", content.Storage);
        Assert.Equal("Running", content.Workers);
    }

    private class HealthResponse
    {
        public string Status { get; set; } = string.Empty;
    }

    private class HealthReadyResponse
    {
        public string Status { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string Migrations { get; set; } = string.Empty;
        public string Config { get; set; } = string.Empty;
        public string Storage { get; set; } = string.Empty;
        public string Workers { get; set; } = string.Empty;
    }
}
