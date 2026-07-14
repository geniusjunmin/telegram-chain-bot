using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database.Models;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TelegramChainBot.Database;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Chain> Chains => Set<Chain>();
    public DbSet<ChainMember> ChainMembers => Set<ChainMember>();
    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();
    public DbSet<ProcessedTelegramUpdate> ProcessedTelegramUpdates => Set<ProcessedTelegramUpdate>();
    public DbSet<ManagedChat> ManagedChats => Set<ManagedChat>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var converter = new DateTimeOffsetToStringConverter();

        modelBuilder.Entity<ProcessedTelegramUpdate>(entity =>
        {
            entity.ToTable("processed_telegram_updates");
            entity.HasKey(x => x.UpdateId);
            entity.Property(x => x.UpdateId).ValueGeneratedNever();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ReceivedAt).HasConversion(converter).IsRequired();
            entity.Property(x => x.ProcessedAt).HasConversion(converter);
        });

        modelBuilder.Entity<AdminAccount>(entity =>
        {
            entity.ToTable("admin_accounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(64).IsRequired();
            entity.Property(x => x.NormalizedUsername).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.NormalizedUsername).IsUnique();
            entity.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Role).HasConversion<int>().IsRequired();
            entity.Property(x => x.LockoutEnd).HasConversion(converter);
            entity.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
            entity.Property(x => x.LastLoginAt).HasConversion(converter);
            entity.Property(x => x.PasswordChangedAt).HasConversion(converter);
            entity.Property(x => x.CreatedAt).HasConversion(converter).IsRequired();
            entity.Property(x => x.UpdatedAt).HasConversion(converter).IsRequired();
        });

        modelBuilder.Entity<Chain>(entity =>
        {
            entity.ToTable("chains");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PublicId).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.PublicId).IsUnique();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.ExpiresAt).HasConversion(converter);
            entity.Property(x => x.CreatedAt).HasConversion(converter).IsRequired();
            entity.Property(x => x.UpdatedAt).HasConversion(converter).IsRequired();
            entity.Property(x => x.ClosedAt).HasConversion(converter);
            entity.Property(x => x.DeletedAt).HasConversion(converter);
            entity.Property(x => x.TelegramSyncStatus).HasConversion<int>().IsRequired();
            entity.Property(x => x.LastSyncedAt).HasConversion(converter);
            entity.Property(x => x.Version).IsConcurrencyToken().IsRequired();
        });

        modelBuilder.Entity<ChainMember>(entity =>
        {
            entity.ToTable("chain_members");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.TelegramUsername).HasMaxLength(128);
            entity.Property(x => x.TelegramFullName).HasMaxLength(256);
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.JoinedAt).HasConversion(converter).IsRequired();
            entity.Property(x => x.UpdatedAt).HasConversion(converter).IsRequired();
            entity.Property(x => x.LeftAt).HasConversion(converter);
            entity.Property(x => x.RemovedAt).HasConversion(converter);
            entity.HasIndex(x => new { x.ChainId, x.TelegramUserId }).IsUnique();
        });

        modelBuilder.Entity<ManagedChat>(entity =>
        {
            entity.ToTable("managed_chats");
            entity.HasKey(x => x.ChatId);
            entity.Property(x => x.ChatId).ValueGeneratedNever();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ChatType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.AuthorizationStatus).HasConversion<int>().IsRequired();
            entity.Property(x => x.CreatePolicy).HasConversion<int>().IsRequired();
            entity.Property(x => x.LastSeenAt).HasConversion(converter).IsRequired();
            entity.Property(x => x.ApprovedAt).HasConversion(converter);
            entity.Property(x => x.BlockedAt).HasConversion(converter);
            entity.Property(x => x.CreatedAt).HasConversion(converter).IsRequired();
            entity.Property(x => x.UpdatedAt).HasConversion(converter).IsRequired();
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActorType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(64).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.BeforeJson).IsRequired();
            entity.Property(x => x.AfterJson).IsRequired();
            entity.Property(x => x.IpAddressHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.UserAgent).HasMaxLength(512).IsRequired();
            entity.Property(x => x.CreatedAt).HasConversion(converter).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.FailureReason).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("system_settings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.WhitelistMode).HasConversion<int>().IsRequired();
            entity.Property(x => x.DefaultCreatePolicy).HasConversion<int>().IsRequired();
            entity.Property(x => x.UnauthorizedChatBehavior).HasMaxLength(64).IsRequired();
            entity.Property(x => x.UpdatedAt).HasConversion(converter).IsRequired();
        });
    }
}
