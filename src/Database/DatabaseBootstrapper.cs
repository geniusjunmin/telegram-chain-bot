using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
            logger.LogError(ex, "Failed to check if __EFMigrationsHistory table exists. Proceeding with standard migration.");
            await db.Database.MigrateAsync(cancellationToken);
            return;
        }

        if (hasMigrationHistoryTable)
        {
            logger.LogInformation("Database contains migration history table. Running migrations.");
            await db.Database.MigrateAsync(cancellationToken);
            return;
        }

        // Legacy database takeover case
        logger.LogWarning("Legacy database detected without EFMigrationsHistory. Initiating takeover.");

        // 1. Back up the old database file
        try
        {
            var backupPath = dbPath + ".bak." + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            File.Copy(dbPath, backupPath, overwrite: true);
            logger.LogInformation("Successfully backed up legacy database to {BackupPath}.", backupPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to back up legacy database. Takeover aborted to protect data.");
            throw;
        }

        // 2. Insert initial migration record
        try
        {
            var allMigrations = db.Database.GetMigrations();
            var initialMigrationId = allMigrations.FirstOrDefault();

            if (initialMigrationId != null)
            {
                logger.LogInformation("Registering initial migration {MigrationId} as already applied.", initialMigrationId);

                // Create __EFMigrationsHistory table
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
                    "\"MigrationId\" TEXT NOT NULL PRIMARY KEY, " +
                    "\"ProductVersion\" TEXT NOT NULL" +
                    ");", cancellationToken);

                // Insert initial migration record
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO \"__EFMigrationsHistory\" (MigrationId, ProductVersion) VALUES ({0}, '9.0.0');",
                    new[] { initialMigrationId },
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register initial migration in __EFMigrationsHistory.");
            throw;
        }

        // 3. Run remaining migrations
        logger.LogInformation("Takeover mapping complete. Applying remaining migrations.");
        await db.Database.MigrateAsync(cancellationToken);

        // 4. Configure WAL mode
        try
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
            logger.LogInformation("Successfully enabled SQLite WAL journal mode.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enable SQLite WAL journal mode.");
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
