using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;
using Xunit;

namespace TelegramChainBot.UnitTests;

public class DatabaseBootstrapperTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly string _backupDir;

    public DatabaseBootstrapperTests()
    {
        var runId = Guid.NewGuid().ToString("N");
        _testDir = Path.Combine(Path.GetTempPath(), $"takeover_test_{runId}");
        Directory.CreateDirectory(_testDir);
        _dbPath = Path.Combine(_testDir, "chain.db");
        _connectionString = $"Data Source={_dbPath};Default Timeout=5;";
        _backupDir = Path.Combine(_testDir, "backups");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    private async Task CreateTrueLegacyDbAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE admins (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL,
                PasswordHash TEXT NOT NULL
            );
            CREATE TABLE chains (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                CreatorId INTEGER NOT NULL,
                MessageId INTEGER NOT NULL,
                ChatId INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE chain_members (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChainId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                Username TEXT NOT NULL,
                TelegramNickname TEXT NOT NULL DEFAULT '',
                JoinTime TEXT NOT NULL
            );

            INSERT INTO admins (Username, PasswordHash) VALUES ('legacy_admin', 'LEGACY_SHA256_HASH_VAL');
            INSERT INTO chains (Title, CreatorId, MessageId, ChatId, CreatedAt) VALUES ('Legacy Chain 1', 1111, 2222, 3333, '2026-07-14T12:00:00Z');
            INSERT INTO chains (Title, CreatorId, MessageId, ChatId, CreatedAt) VALUES ('Legacy Chain 2', 4444, 5555, 6666, '2026-07-14T12:00:00Z');
            INSERT INTO chain_members (ChainId, UserId, Username, TelegramNickname, JoinTime) VALUES (1, 999, 'user999', 'nick999', '2026-07-14T12:05:00Z');
        ";
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Takeover_Succeeds_MigratesLegacyDataCorrectly()
    {
        await CreateTrueLegacyDbAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        await using (var db = new AppDbContext(options))
        {
            var bootstrapper = new DatabaseBootstrapper(db, NullLogger<DatabaseBootstrapper>.Instance);

            await bootstrapper.BootstrapAsync();

            var migrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains("20260714120000_LegacyBaseline", migrations);
            Assert.Contains("20260714130000_LegacySchemaUpgrade", migrations);

            var chain1 = await db.Chains.FirstOrDefaultAsync(c => c.Title == "Legacy Chain 1");
            Assert.NotNull(chain1);
            Assert.Equal(1111, chain1.CreatorTelegramUserId);
            Assert.Equal(3333, chain1.ChatId);
            Assert.Equal(ChainStatus.Active, chain1.Status);
            Assert.Equal(TelegramSyncStatus.Synced, chain1.TelegramSyncStatus);
            Assert.False(string.IsNullOrEmpty(chain1.PublicId));
            Assert.Equal(32, chain1.PublicId.Length);

            var chain2 = await db.Chains.FirstOrDefaultAsync(c => c.Title == "Legacy Chain 2");
            Assert.NotNull(chain2);
            Assert.NotEqual(chain1.PublicId, chain2.PublicId);

            var member = await db.ChainMembers.FirstOrDefaultAsync(m => m.ChainId == chain1.Id);
            Assert.NotNull(member);
            Assert.Equal(999, member.TelegramUserId);
            Assert.Equal("user999", member.DisplayName);
            Assert.Equal("nick999", member.TelegramUsername);
            Assert.Equal(ChainMemberStatus.Active, member.Status);

            var admin = await db.AdminAccounts.FirstOrDefaultAsync(a => a.Username == "legacy_admin");
            Assert.NotNull(admin);
            Assert.Equal("LEGACY_SHA256_HASH_VAL", admin.PasswordHash);
            Assert.Equal(AdminRole.AuditorAdmin, admin.Role);
            Assert.True(admin.IsActive);

            var chat1 = await db.ManagedChats.FindAsync(3333L);
            Assert.NotNull(chat1);
            var chat2 = await db.ManagedChats.FindAsync(6666L);
            Assert.NotNull(chat2);
        }

        var backups = Directory.GetFiles(_backupDir, "pre-takeover-*.db");
        Assert.Single(backups);

        SqliteBackupHelper.VerifyBackup(backups[0]);
    }

    [Fact]
    public async Task Takeover_Throws_WhenLegacyStructureIsInvalid()
    {
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE admins (Id INTEGER PRIMARY KEY AUTOINCREMENT);
                CREATE TABLE chains (Id INTEGER PRIMARY KEY AUTOINCREMENT);
                CREATE TABLE chain_members (Id INTEGER PRIMARY KEY AUTOINCREMENT);
            ";
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        await using var db = new AppDbContext(options);
        var bootstrapper = new DatabaseBootstrapper(db, NullLogger<DatabaseBootstrapper>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => bootstrapper.BootstrapAsync());
    }

    [Fact]
    public async Task Takeover_Idempotent_SubsequentRunsDoNotModifySchemaOrData()
    {
        await CreateTrueLegacyDbAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        await using (var db = new AppDbContext(options))
        {
            var bootstrapper = new DatabaseBootstrapper(db, NullLogger<DatabaseBootstrapper>.Instance);
            
            await bootstrapper.BootstrapAsync();
            await bootstrapper.BootstrapAsync();
        }

        var backups = Directory.GetFiles(_backupDir, "pre-takeover-*.db");
        Assert.Single(backups);
    }
}
