using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
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

        var existingChat = await db.ManagedChats.FindAsync(0L);
        if (existingChat != null)
        {
            existingChat.AuthorizationStatus = AuthorizationStatus.Approved;
        }
        else
        {
            var chat = new ManagedChat
            {
                ChatId = 0,
                Title = "Test Group",
                ChatType = "group",
                AuthorizationStatus = AuthorizationStatus.Approved,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.ManagedChats.Add(chat);
        }

        var chain = new Chain
        {
            PublicId = Guid.NewGuid().ToString("N"),
            Title = "Dinner Join Test",
            CreatorTelegramUserId = 12345,
            ChatId = 0,
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

    [Fact]
    public async Task JoinChain_SavesXssPayloadWithoutCrashing()
    {
        // Arrange
        var client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existingChat = await db.ManagedChats.FindAsync(0L);
        if (existingChat != null)
        {
            existingChat.AuthorizationStatus = AuthorizationStatus.Approved;
        }
        else
        {
            var chat = new ManagedChat
            {
                ChatId = 0,
                Title = "XSS Test Group",
                ChatType = "group",
                AuthorizationStatus = AuthorizationStatus.Approved,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.ManagedChats.Add(chat);
        }

        var chain = new Chain
        {
            PublicId = Guid.NewGuid().ToString("N"),
            Title = "XSS Test Chain",
            CreatorTelegramUserId = 22222,
            ChatId = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Chains.Add(chain);
        await db.SaveChangesAsync();

        var userJson = "{\"id\":22222,\"first_name\":\"Attacker\",\"username\":\"attacker\"}";
        var authDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var botToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";
        var initData = GenerateValidInitData(botToken, userJson, authDate);

        var xssPayload = "<script>alert('XSS')</script>";
        var requestBody = new ChainController.JoinRequest(xssPayload);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/chains/{chain.PublicId}/join")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add("X-Telegram-Init-Data", initData);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JoinResponse>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(xssPayload, result.UpdatedMembers[0].DisplayName);
    }

    [Fact]
    public async Task Headers_Include_ContentSecurityPolicy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("default-src 'self'; script-src 'self' https://telegram.org 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self';",
            response.Headers.GetValues("Content-Security-Policy").First());

        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());

        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());
    }

    [Fact]
    public async Task UncaughtException_ReturnsProblemDetails()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/debug/error");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(500, problem.Status);
        Assert.Equal("An error occurred while processing your request.", problem.Title);
        Assert.Equal("/api/debug/error", problem.Instance);
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

    [Fact]
    public async Task Admin_CanAccessStatsAndSettings_AfterLoginAndPasswordChange()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        // 1. Get initial CSRF token
        var csrfResponse1 = await client.GetAsync("/api/admin/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse1.StatusCode);
        var xsrfToken1 = GetXsrfToken(csrfResponse1);

        // 2. Login
        var loginReq = new AdminController.LoginRequest("admin", "SuperSecureAcc123!");
        var loginMsg = new HttpRequestMessage(HttpMethod.Post, "/api/admin/login")
        {
            Content = JsonContent.Create(loginReq)
        };
        if (xsrfToken1 != null)
        {
            loginMsg.Headers.Add("X-XSRF-TOKEN", xsrfToken1);
        }
        var loginResponse = await client.SendAsync(loginMsg);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // 3. Get fresh post-login CSRF token
        var csrfResponse2 = await client.GetAsync("/api/admin/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse2.StatusCode);
        var xsrfToken2 = GetXsrfToken(csrfResponse2);

        // 4. Change password
        var changePwdReq = new AdminController.ChangePasswordRequest("SuperSecureAcc123!", "NewSuperSecureAcc123!");
        var changePwdMsg = new HttpRequestMessage(HttpMethod.Post, "/api/admin/change-password")
        {
            Content = JsonContent.Create(changePwdReq)
        };
        if (xsrfToken2 != null)
        {
            changePwdMsg.Headers.Add("X-XSRF-TOKEN", xsrfToken2);
        }
        var changeResponse = await client.SendAsync(changePwdMsg);
        var changeResponseBody = await changeResponse.Content.ReadAsStringAsync();
        Assert.True(changeResponse.StatusCode == HttpStatusCode.OK, $"Failed: {changeResponse.StatusCode}. Body: {changeResponseBody}");

        // 4. Get fresh CSRF token for the next login (session fixation protection)
        var csrfResponse3 = await client.GetAsync("/api/admin/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse3.StatusCode);
        var xsrfToken3 = GetXsrfToken(csrfResponse3);

        // 5. Login again with the NEW password
        var loginReq2 = new AdminController.LoginRequest("admin", "NewSuperSecureAcc123!");
        var loginMsg2 = new HttpRequestMessage(HttpMethod.Post, "/api/admin/login")
        {
            Content = JsonContent.Create(loginReq2)
        };
        if (xsrfToken3 != null)
        {
            loginMsg2.Headers.Add("X-XSRF-TOKEN", xsrfToken3);
        }
        var loginResponse2 = await client.SendAsync(loginMsg2);
        Assert.Equal(HttpStatusCode.OK, loginResponse2.StatusCode);

        // 6. Get fresh post-login CSRF token
        var csrfResponse4 = await client.GetAsync("/api/admin/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse4.StatusCode);
        var xsrfToken4 = GetXsrfToken(csrfResponse4);

        // 6.5 Verify auth/me endpoint
        var meResponse = await client.GetAsync("/api/admin/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var meObj = await meResponse.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.NotNull(meObj);
        Assert.Equal("admin", meObj["username"]?.ToString());
        Assert.Equal("RootAdmin", meObj["role"]?.ToString());

        // 7. Access dashboard-stats
        var statsResponse = await client.GetAsync("/api/admin/dashboard-stats");
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);
        var stats = await statsResponse.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.NotNull(stats);
        Assert.True(stats.ContainsKey("total_groups"));
        Assert.True(stats.ContainsKey("total_active_chains"));

        // 5. Access system-settings GET
        var settingsResponse = await client.GetAsync("/api/admin/system-settings");
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
        var settings = await settingsResponse.Content.ReadFromJsonAsync<SystemSetting>();
        Assert.NotNull(settings);
        Assert.Equal(1, settings.Id);

        // 6. Update system-settings POST
        settings.DefaultMaxMembers = 88;
        var updateSettingsMsg = new HttpRequestMessage(HttpMethod.Post, "/api/admin/system-settings")
        {
            Content = JsonContent.Create(settings)
        };
        if (xsrfToken4 != null)
        {
            updateSettingsMsg.Headers.Add("X-XSRF-TOKEN", xsrfToken4);
        }
        var updateResponse = await client.SendAsync(updateSettingsMsg);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Verify updated setting
        var getResponse = await client.GetAsync("/api/admin/system-settings");
        var updatedSettings = await getResponse.Content.ReadFromJsonAsync<SystemSetting>();
        Assert.Equal(88, updatedSettings?.DefaultMaxMembers);
    }

    private static string? GetXsrfToken(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var cookie in setCookies)
            {
                if (cookie.Contains("XSRF-TOKEN="))
                {
                    var start = cookie.IndexOf("XSRF-TOKEN=") + "XSRF-TOKEN=".Length;
                    var end = cookie.IndexOf(';', start);
                    return end >= 0 ? cookie[start..end] : cookie[start..];
                }
            }
        }
        return null;
    }

    [Fact]
    public async Task Health_Live_ReturnsHealthy()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var res = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.Equal("Healthy", res?["status"]?.ToString());
    }

    [Fact]
    public async Task Health_Ready_ReturnsHealthy()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var res = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.Equal("Healthy", res?["status"]?.ToString());
        Assert.Equal("Connected", res?["database"]?.ToString());
    }

    [Fact]
    public async Task FirstLogin_MustChangePassword_Flow()
    {
        // Isolate database state by inserting a specific admin account
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<AdminAccount>>();

            var existing = db.AdminAccounts.FirstOrDefault(a => a.Username == "firstloginadmin");
            if (existing != null)
            {
                db.AdminAccounts.Remove(existing);
                db.SaveChanges();
            }

            var account = new AdminAccount
            {
                Username = "firstloginadmin",
                NormalizedUsername = "FIRSTLOGINADMIN",
                Role = AdminRole.RootAdmin,
                IsActive = true,
                MustChangePassword = true,
                PasswordHash = string.Empty,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            account.PasswordHash = hasher.HashPassword(account, "SuperSecureAcc123!");
            db.AdminAccounts.Add(account);
            db.SaveChanges();
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        // 1. Get initial CSRF token
        var csrfResponse1 = await client.GetAsync("/api/admin/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse1.StatusCode);
        var xsrfToken1 = GetXsrfToken(csrfResponse1);

        // 2. Login with initial admin password (which has MustChangePassword = true)
        var loginReq = new AdminController.LoginRequest("firstloginadmin", "SuperSecureAcc123!");
        var loginMsg = new HttpRequestMessage(HttpMethod.Post, "/api/admin/login")
        {
            Content = JsonContent.Create(loginReq)
        };
        if (xsrfToken1 != null)
        {
            loginMsg.Headers.Add("X-XSRF-TOKEN", xsrfToken1);
        }
        var loginResponse = await client.SendAsync(loginMsg);
        var body = await loginResponse.Content.ReadAsStringAsync();
        Assert.True(loginResponse.StatusCode == HttpStatusCode.OK, $"Login failed: {loginResponse.StatusCode}. Body: {body}");

        // 3. auth/me is allowed and returns mustChangePassword = true
        var meResponse = await client.GetAsync("/api/admin/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var meObj = await meResponse.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.NotNull(meObj);
        Assert.Equal("firstloginadmin", meObj["username"]?.ToString());
        Assert.Equal("True", meObj["mustChangePassword"]?.ToString(), ignoreCase: true);

        // 4. dashboard is blocked (403 Forbidden) because password has not been changed yet
        var statsResponse = await client.GetAsync("/api/admin/dashboard-stats");
        Assert.Equal(HttpStatusCode.Forbidden, statsResponse.StatusCode);

        // 5. Get fresh post-login CSRF token
        var csrfResponse2 = await client.GetAsync("/api/admin/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse2.StatusCode);
        var xsrfToken2 = GetXsrfToken(csrfResponse2);

        // 6. Change password (change-password endpoint is allowed)
        var changePwdReq = new AdminController.ChangePasswordRequest("SuperSecureAcc123!", "NewSuperSecureAcc123!");
        var changePwdMsg = new HttpRequestMessage(HttpMethod.Post, "/api/admin/change-password")
        {
            Content = JsonContent.Create(changePwdReq)
        };
        if (xsrfToken2 != null)
        {
            changePwdMsg.Headers.Add("X-XSRF-TOKEN", xsrfToken2);
        }
        var changeResponse = await client.SendAsync(changePwdMsg);
        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

        // 7. Try to call auth/me using the old session cookie -> must return 401 Unauthorized (because SecurityStamp changed and session got invalidated)
        var meResponseAfterChange = await client.GetAsync("/api/admin/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseAfterChange.StatusCode);

        // 8. Get fresh CSRF token for login
        var csrfResponse3 = await client.GetAsync("/api/admin/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse3.StatusCode);
        var xsrfToken3 = GetXsrfToken(csrfResponse3);

        // 9. Login again with the new password
        var loginReq2 = new AdminController.LoginRequest("firstloginadmin", "NewSuperSecureAcc123!");
        var loginMsg2 = new HttpRequestMessage(HttpMethod.Post, "/api/admin/login")
        {
            Content = JsonContent.Create(loginReq2)
        };
        if (xsrfToken3 != null)
        {
            loginMsg2.Headers.Add("X-XSRF-TOKEN", xsrfToken3);
        }
        var loginResponse2 = await client.SendAsync(loginMsg2);
        Assert.Equal(HttpStatusCode.OK, loginResponse2.StatusCode);

        // 10. auth/me is allowed and returns mustChangePassword = false
        var meResponse2 = await client.GetAsync("/api/admin/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse2.StatusCode);
        var meObj2 = await meResponse2.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        Assert.NotNull(meObj2);
        Assert.Equal("False", meObj2["mustChangePassword"]?.ToString(), ignoreCase: true);

        // 11. dashboard is now allowed (200 OK)
        var statsResponse2 = await client.GetAsync("/api/admin/dashboard-stats");
        Assert.Equal(HttpStatusCode.OK, statsResponse2.StatusCode);
    }
}
