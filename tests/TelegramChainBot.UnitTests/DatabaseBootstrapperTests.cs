using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;
using Xunit;

namespace TelegramChainBot.UnitTests;

public class DatabaseBootstrapperTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public DatabaseBootstrapperTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_takeover_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    public void Dispose()
    {
        // Clean up main database file
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }

        // Clean up backups
        var directory = Path.GetDirectoryName(_dbPath);
        if (directory != null)
        {
            var fileName = Path.GetFileName(_dbPath);
            var backups = Directory.GetFiles(directory, $"{fileName}.bak.*");
            foreach (var backup in backups)
            {
                try { File.Delete(backup); } catch { }
            }
        }
    }

    [Fact]
    public async Task Takeover_Succeeds_WhenLegacyDatabaseExistsWithoutMigrationHistory()
    {
        // 1. Create a legacy database using raw ADO.NET
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    CREATE TABLE chains (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PublicId TEXT NOT NULL,
                        ChatId INTEGER NOT NULL DEFAULT 0,
                        MessageId INTEGER NOT NULL DEFAULT 0,
                        Title TEXT NOT NULL,
                        CreatorId INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        Status TEXT NOT NULL DEFAULT 'open',
                        MaxMembers INTEGER NOT NULL DEFAULT 20,
                        ExpiresAt TEXT NULL
                    );
                    CREATE TABLE chain_members (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ChainId INTEGER NOT NULL,
                        UserId INTEGER NOT NULL,
                        Username TEXT NOT NULL,
                        TelegramNickname TEXT NOT NULL DEFAULT '',
                        JoinTime TEXT NOT NULL
                    );
                    CREATE TABLE admins (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Username TEXT NOT NULL,
                        PasswordHash TEXT NOT NULL
                    );
                    INSERT INTO chains (PublicId, Title, CreatorId, CreatedAt) VALUES ('test_public_id', 'Legacy Dinner', 12345, '2026-07-12T20:00:00Z');
                    INSERT INTO admins (Username, PasswordHash) VALUES ('legacy_admin', 'LEGACY_SHA256_HASH_VAL');
                ";
                await command.ExecuteNonQueryAsync();
            }
        }

        // 2. Setup EF Core Context pointing to this legacy database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        await using (var db = new AppDbContext(options))
        {
            var bootstrapper = new DatabaseBootstrapper(db, NullLogger<DatabaseBootstrapper>.Instance);

            // 3. Run bootstrapper
            await bootstrapper.BootstrapAsync();

            // 4. Verify __EFMigrationsHistory exists and has the migration
            var migrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.NotEmpty(migrations);

            // 5. Verify data is preserved
            var chain = await db.Chains.FirstOrDefaultAsync(c => c.PublicId == "test_public_id");
            Assert.NotNull(chain);
            Assert.Equal("Legacy Dinner", chain.Title);
        }

        // 6. Verify backup file was created
        var directory = Path.GetDirectoryName(_dbPath)!;
        var fileName = Path.GetFileName(_dbPath);
        var backups = Directory.GetFiles(directory, $"{fileName}.bak.*");
        Assert.Single(backups);
    }
}
