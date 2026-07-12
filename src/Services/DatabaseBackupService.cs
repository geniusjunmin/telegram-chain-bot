using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TelegramChainBot.Services;

public sealed class DatabaseBackupService(IConfiguration configuration, ILogger<DatabaseBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DatabaseBackupService is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformBackupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during scheduled database backup.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("DatabaseBackupService is stopping.");
    }

    private Task PerformBackupAsync(CancellationToken cancellationToken)
    {
        var dbPath = configuration["SQLITE_PATH"] ?? "data/chain.db";
        var backupDir = configuration["BACKUP_PATH"] ?? "data/backups";

        if (!File.Exists(dbPath))
        {
            logger.LogWarning("SQLite database file {DbPath} does not exist. Skipping backup.", dbPath);
            return Task.CompletedTask;
        }

        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"chain_{timestamp}.db";
        var backupFilePath = Path.Combine(backupDir, backupFileName);

        logger.LogInformation("Backing up SQLite database from {DbPath} to {BackupFilePath}...", dbPath, backupFilePath);
        File.Copy(dbPath, backupFilePath, overwrite: true);
        logger.LogInformation("Backup completed successfully.");

        try
        {
            var files = Directory.GetFiles(backupDir, "chain_*.db");
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTimeUtc < DateTime.UtcNow.AddDays(-7))
                {
                    logger.LogInformation("Deleting outdated backup file: {FilePath}", file);
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clean up old backup files.");
        }

        return Task.CompletedTask;
    }
}
