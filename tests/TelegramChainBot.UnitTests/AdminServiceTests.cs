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
        await File.WriteAllTextAsync(tempFile, "SuperStrongPassword123!");
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD_FILE", tempFile);

        try
        {
            var service = new AdminService(_db, _hasher);
            await service.EnsureDefaultAdminAsync(CancellationToken.None);

            var admin = await _db.AdminAccounts.FirstOrDefaultAsync(a => a.Username == "admin");
            Assert.NotNull(admin);
            Assert.True(admin.MustChangePassword);
            Assert.True(admin.IsActive);
            
            var verifyResult = _hasher.VerifyHashedPassword(admin, admin.PasswordHash, "SuperStrongPassword123!");
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
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD", "AnotherStrongPassword123!");

        var service = new AdminService(_db, _hasher);
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

        var service = new AdminService(_db, _hasher);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureDefaultAdminAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Bootstrap_Throws_WhenPasswordTooSimple()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD", "1111111111111111");

        var service = new AdminService(_db, _hasher);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureDefaultAdminAsync(CancellationToken.None));
    }
}
