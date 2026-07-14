using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramChainBot.Database;

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
                await PerformScheduledBackupsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during scheduled database backup run.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("DatabaseBackupService is stopping.");
    }

    public async Task PerformScheduledBackupsAsync(CancellationToken cancellationToken)
    {
        var dbPath = configuration["SQLITE_PATH"] ?? "data/chain.db";
        var backupDir = configuration["BACKUP_PATH"] ?? "data/backups";
        var connectionString = $"Data Source={dbPath};Default Timeout=5;";

        if (!File.Exists(dbPath))
        {
            logger.LogWarning("SQLite database file {DbPath} does not exist. Skipping backup.", dbPath);
            return;
        }

        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        var now = DateTimeOffset.UtcNow;
        var todayStr = now.ToString("yyyyMMdd");
        var thisWeekStr = GetWeekOfYearString(now);

        var dailyBackupName = $"chain_daily_{todayStr}.db";
        var dailyBackupPath = Path.Combine(backupDir, dailyBackupName);

        if (!File.Exists(dailyBackupPath))
        {
            logger.LogInformation("Creating daily database backup: {BackupName}", dailyBackupName);
            try
            {
                SqliteBackupHelper.Backup(connectionString, dailyBackupPath, logger);
                logger.LogInformation("Daily backup created successfully.");
                
                CleanupOldBackups(backupDir, "chain_daily_*.db", 7);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create daily backup. Integrity check failed or SQLite error occurred.");
            }
        }

        if (now.DayOfWeek == DayOfWeek.Sunday)
        {
            var weeklyBackupName = $"chain_weekly_{thisWeekStr}.db";
            var weeklyBackupPath = Path.Combine(backupDir, weeklyBackupName);

            if (!File.Exists(weeklyBackupPath))
            {
                logger.LogInformation("Creating weekly database backup: {BackupName}", weeklyBackupName);
                try
                {
                    SqliteBackupHelper.Backup(connectionString, weeklyBackupPath, logger);
                    logger.LogInformation("Weekly backup created successfully.");

                    CleanupOldBackups(backupDir, "chain_weekly_*.db", 4);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create weekly backup. Integrity check failed or SQLite error occurred.");
                }
            }
        }
    }

    private void CleanupOldBackups(string dir, string searchPattern, int maxCountToKeep)
    {
        try
        {
            var files = Directory.GetFiles(dir, searchPattern)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.Name)
                .ToList();

            if (files.Count <= maxCountToKeep)
            {
                return;
            }

            var toDelete = files.Skip(maxCountToKeep).ToList();

            foreach (var fileInfo in toDelete)
            {
                logger.LogInformation("Deleting outdated backup file: {FilePath}", fileInfo.FullName);
                File.Delete(fileInfo.FullName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clean up old backup files matching {Pattern}", searchPattern);
        }
    }

    private static string GetWeekOfYearString(DateTimeOffset time)
    {
        var calendar = System.Globalization.CultureInfo.InvariantCulture.Calendar;
        int week = calendar.GetWeekOfYear(time.UtcDateTime, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"{time.Year}_W{week:D2}";
    }
}
