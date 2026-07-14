using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace TelegramChainBot.Database;

public static class SqliteBackupHelper
{
    public static void Backup(string sourceConnectionString, string destinationPath, ILogger? logger = null)
    {
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        logger?.LogInformation("Running SQLite VACUUM INTO backup to {DestinationPath}", destinationPath);

        using (var connection = new SqliteConnection(sourceConnectionString))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO @destPath;";
            command.Parameters.AddWithValue("@destPath", destinationPath);
            command.ExecuteNonQuery();
        }

        VerifyBackup(destinationPath, logger);
    }

    public static void VerifyBackup(string path, ILogger? logger = null)
    {
        var destConnString = $"Data Source={path};Default Timeout=5;";
        using var connection = new SqliteConnection(destConnString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = command.ExecuteScalar()?.ToString();
        
        logger?.LogInformation("Integrity check result for backup {Path}: {Result}", path, result);
        
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SQLite backup integrity check failed for {path}: {result}");
        }
    }
}
