using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Database;

public sealed class DatabaseBootstrapper(
    AppDbContext db,
    ILogger<DatabaseBootstrapper> logger)
{
    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = db.Database.GetConnectionString();
        var dbPath = ParseDbPath(connectionString);

        if (string.IsNullOrWhiteSpace(dbPath) || string.Equals(dbPath, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Database is in-memory or connection string is empty. Running migrations directly.");
            await db.Database.MigrateAsync(cancellationToken);
            return;
        }

        var dbFileExists = File.Exists(dbPath);
        if (!dbFileExists)
        {
            logger.LogInformation("Database file does not exist. Running migrations to create a new database.");
            await db.Database.MigrateAsync(cancellationToken);
            return;
        }

        // Database file exists. Check if __EFMigrationsHistory table exists.
        bool hasMigrationHistoryTable;
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            hasMigrationHistoryTable = Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check if __EFMigrationsHistory table exists. Aborting.");
            throw;
        }

        if (hasMigrationHistoryTable)
        {
            logger.LogInformation("Database contains migration history table. Running migrations.");
            await db.Database.MigrateAsync(cancellationToken);
            return;
        }

        // Legacy database takeover case
        logger.LogWarning("Legacy database detected without EFMigrationsHistory. Initiating takeover.");

        // 1. Verify legacy table structures
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var tables = new[] { "admins", "chains", "chain_members" };
            foreach (var table in tables)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
                command.Parameters.AddWithValue("@name", table);
                var exists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
                if (!exists)
                {
                    throw new InvalidOperationException($"Legacy table '{table}' is missing.");
                }
            }

            await using var colCommand = connection.CreateCommand();
            colCommand.CommandText = "PRAGMA table_info(chains);";
            await using var reader = await colCommand.ExecuteReaderAsync(cancellationToken);
            bool hasCreatorId = false;
            bool hasChatId = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                var colName = reader["name"].ToString();
                if (string.Equals(colName, "CreatorId", StringComparison.OrdinalIgnoreCase)) hasCreatorId = true;
                if (string.Equals(colName, "ChatId", StringComparison.OrdinalIgnoreCase)) hasChatId = true;
            }
            if (!hasCreatorId || !hasChatId)
            {
                throw new InvalidOperationException("Legacy 'chains' table structure is invalid.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Legacy database structure validation failed. Aborting startup.");
            throw;
        }

        // 2. Back up the old database file using secure SQLite Backup / VACUUM INTO
        try
        {
            var backupDir = Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "backups");
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var backupPath = Path.Combine(backupDir, $"pre-takeover-{timestamp}.db");

            SqliteBackupHelper.Backup(connectionString!, backupPath, logger);
            logger.LogInformation("Successfully backed up legacy database to {BackupPath}.", backupPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to back up legacy database. Takeover aborted to protect data.");
            throw;
        }

        // 3. Insert LegacyBaseline migration record
        try
        {
            logger.LogInformation("Registering LegacyBaseline migration as already applied.");

            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
                "\"MigrationId\" TEXT NOT NULL PRIMARY KEY, " +
                "\"ProductVersion\" TEXT NOT NULL" +
                ");", cancellationToken);

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"__EFMigrationsHistory\" (MigrationId, ProductVersion) VALUES ('20260714120000_LegacyBaseline', '10.0.9');",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register baseline migration in __EFMigrationsHistory. Aborting.");
            throw;
        }

        // 4. Run remaining migrations
        logger.LogInformation("Takeover mapping complete. Applying remaining migrations.");
        await db.Database.MigrateAsync(cancellationToken);

        // 5. Configure WAL mode
        try
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
            logger.LogInformation("Successfully enabled SQLite WAL journal mode.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enable SQLite WAL journal mode.");
        }

        // 6. Ensure default system settings and bootstrap BotToken
        var settings = await db.SystemSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings == null)
        {
            settings = new SystemSetting
            {
                Id = 1,
                WhitelistMode = WhitelistMode.Enforced,
                UnauthorizedChatBehavior = "WarnAndLeave",
                DefaultCreatePolicy = CreatePolicy.Everyone,
                DefaultMaxMembers = 100,
                DefaultChainExpiryHours = 24,
                MaxActiveChainsPerChat = 5,
                TelegramInitDataMaxAgeSeconds = 86400,
                DeletedDataRetentionDays = 30,
                RequireMfaForSuperAdmin = false,
                StaticVersion = "1.0.0",
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.SystemSettings.Add(settings);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(settings.BotToken))
        {
            var envToken = Environment.GetEnvironmentVariable("BOT_TOKEN");
            var envTokenFile = Environment.GetEnvironmentVariable("BOT_TOKEN_FILE");
            if (string.IsNullOrWhiteSpace(envToken) && !string.IsNullOrWhiteSpace(envTokenFile) && File.Exists(envTokenFile))
            {
                envToken = (await File.ReadAllTextAsync(envTokenFile, cancellationToken)).Trim();
            }
            if (!string.IsNullOrWhiteSpace(envToken))
            {
                settings.BotToken = envToken;
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Bootstrapped SystemSettings.BotToken from environment configuration.");
            }
        }

        if (settings != null && (settings.StaticVersion == "1.0.0" || string.IsNullOrWhiteSpace(settings.StaticVersion)))
        {
            settings.StaticVersion = "1.0.1";
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Automatically upgraded default StaticVersion to 1.0.1 to clear old browser cache.");
        }
    }

    private static string? ParseDbPath(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            return builder.DataSource;
        }
        catch
        {
            return null;
        }
    }
}
