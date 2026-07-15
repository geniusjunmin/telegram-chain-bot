using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramChainBot.Api;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;
using Xunit;

namespace TelegramChainBot.IntegrationTests;

public class AdminApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AdminApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateSecureClient()
    {
        return _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    private async Task<string> GetXsrfTokenAsync(HttpClient client)
    {
        var csrfRes = await client.GetAsync("/api/admin/csrf");
        csrfRes.EnsureSuccessStatusCode();

        var cookies = csrfRes.Headers.GetValues("Set-Cookie");
        foreach (var cookie in cookies)
        {
            if (cookie.Contains("XSRF-TOKEN="))
            {
                return cookie.Split(';')[0].Substring("XSRF-TOKEN=".Length);
            }
        }
        return string.Empty;
    }

    private async Task AuthenticateClientAsync(HttpClient client)
    {
        // Clear must change password flag for test admin
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = await db.AdminAccounts.FirstOrDefaultAsync(a => a.Username == "admin");
            if (admin != null)
            {
                admin.MustChangePassword = false;
                admin.IsActive = true;
                await db.SaveChangesAsync();
            }
        }

        var xsrfToken = await GetXsrfTokenAsync(client);

        var loginPayload = new AdminController.LoginRequest("admin", "SuperSecureAcc123!");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/login")
        {
            Content = JsonContent.Create(loginPayload)
        };
        if (!string.IsNullOrEmpty(xsrfToken))
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrfToken);
        }

        var loginRes = await client.SendAsync(request);
        loginRes.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetAccounts_ReturnsPaginatedList()
    {
        // Arrange
        var client = CreateSecureClient();
        await AuthenticateClientAsync(client);

        // Act
        var response = await client.GetAsync("/api/admin/accounts?page=1&pageSize=5");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AccountsPage>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.True(result.Total >= 1);
        Assert.True(result.TotalPages >= 1);
    }

    [Fact]
    public async Task CreateAccount_ValidatesPasswordPolicy()
    {
        // Arrange
        var client = CreateSecureClient();
        await AuthenticateClientAsync(client);
        var xsrfToken = await GetXsrfTokenAsync(client);

        // Weak password payload
        var payload = new AdminController.CreateAccountRequest("weak_user", "123", "OperatorAdmin");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/accounts")
        {
            Content = JsonContent.Create(payload)
        };
        if (!string.IsNullOrEmpty(xsrfToken))
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrfToken);
        }

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("密码长度必须至少为 12 个字符", body);
    }

    [Fact]
    public async Task UpdateMember_ReturnsRFCValidationProblem_WhenInvalid()
    {
        // Arrange
        var client = CreateSecureClient();
        await AuthenticateClientAsync(client);
        var xsrfToken = await GetXsrfTokenAsync(client);

        // Setup a chain and member in DB
        long memberId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var chain = new Chain { PublicId = Guid.NewGuid().ToString("N"), Title = "Test Chain" };
            db.Chains.Add(chain);
            await db.SaveChangesAsync();

            var member = new ChainMember { ChainId = chain.Id, DisplayName = "Alice", Status = ChainMemberStatus.Active };
            db.ChainMembers.Add(member);
            await db.SaveChangesAsync();
            memberId = member.Id;
        }

        // Empty display name (invalid)
        var payload = new AdminController.UpdateMemberRequest("", "alice_tg");
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/members/{memberId}")
        {
            Content = JsonContent.Create(payload)
        };
        if (!string.IsNullOrEmpty(xsrfToken))
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrfToken);
        }

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Should contain standard RFC 7807 problem details
        Assert.Contains("errors", body);
        Assert.Contains("Username", body);
    }

    private class AccountsPage
    {
        public List<AccountDto> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public int TotalPages { get; set; }
    }

    private class AccountDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsDisabled { get; set; }
    }
}
