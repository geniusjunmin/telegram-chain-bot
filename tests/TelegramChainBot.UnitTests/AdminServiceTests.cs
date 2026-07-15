using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;
using TelegramChainBot.Services;
using Xunit;

namespace TelegramChainBot.UnitTests;

public class AdminServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _contextOptions;
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AdminAccount> _hasher;
    private readonly AdminPasswordPolicy _passwordPolicy;

    public AdminServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _contextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(_contextOptions);
        _db.Database.EnsureCreated();

        _hasher = new PasswordHasher<AdminAccount>();
        _passwordPolicy = new AdminPasswordPolicy();

        // Isolate INITIAL_ADMIN_PASSWORD_FILE from repo directory
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD_FILE", Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();

        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD_FILE", null);
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Fact]
    public async Task Bootstrap_Succeeds_WhenValidPasswordInFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"pwd_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "SuperStrongAcc123!");
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD_FILE", tempFile);

        try
        {
            var service = new AdminService(_db, _hasher, _passwordPolicy);
            await service.EnsureDefaultAdminAsync(CancellationToken.None);

            var admin = await _db.AdminAccounts.FirstOrDefaultAsync(a => a.Username == "admin");
            Assert.NotNull(admin);
            Assert.True(admin.MustChangePassword);
            Assert.True(admin.IsActive);

            var verifyResult = _hasher.VerifyHashedPassword(admin, admin.PasswordHash, "SuperStrongAcc123!");
            Assert.Equal(PasswordVerificationResult.Success, verifyResult);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Bootstrap_Succeeds_WhenValidPasswordInEnvVarAndDevEnvironment()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD", "AnotherStrongAcc123!");

        var service = new AdminService(_db, _hasher, _passwordPolicy);
        await service.EnsureDefaultAdminAsync(CancellationToken.None);

        var admin = await _db.AdminAccounts.FirstOrDefaultAsync(a => a.Username == "admin");
        Assert.NotNull(admin);
        Assert.True(admin.MustChangePassword);
    }

    [Fact]
    public async Task Bootstrap_Throws_WhenPasswordTooShort()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD", "Short123!");

        var service = new AdminService(_db, _hasher, _passwordPolicy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureDefaultAdminAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Bootstrap_Throws_WhenPasswordTooSimple()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD", "1111111111111111");

        var service = new AdminService(_db, _hasher, _passwordPolicy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureDefaultAdminAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AdminSessionService_CanRegisterAndVerifySession()
    {
        var sessionService = new AdminSessionService(_db);
        var sessionId = await sessionService.RegisterSessionAsync(1, TimeSpan.FromHours(1), CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(sessionId));

        var isActive = await sessionService.IsSessionActiveAsync(sessionId, CancellationToken.None);
        Assert.True(isActive);

        var adminId = await sessionService.GetAdminIdAsync(sessionId, CancellationToken.None);
        Assert.Equal(1, adminId);

        await sessionService.RevokeSessionAsync(sessionId, CancellationToken.None);
        var isActiveAfterRevoke = await sessionService.IsSessionActiveAsync(sessionId, CancellationToken.None);
        Assert.False(isActiveAfterRevoke);
    }
}
