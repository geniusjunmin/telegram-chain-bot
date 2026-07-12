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
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Role).HasConversion<int>().IsRequired();
            entity.Property(x => x.LockoutEnd).HasConversion(converter);
        });

        modelBuilder.Entity<Chain>(entity =>
        {
            entity.ToTable("chains");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PublicId).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.PublicId).IsUnique();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedAt).HasConversion(converter).IsRequired();
        });

        modelBuilder.Entity<ChainMember>(entity =>
        {
            entity.ToTable("chain_members");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(128).IsRequired();
            entity.Property(x => x.TelegramNickname).HasMaxLength(128).HasDefaultValue(string.Empty).IsRequired();
            entity.Property(x => x.JoinTime).HasConversion(converter).IsRequired();
            entity.HasIndex(x => new { x.ChainId, x.UserId }).IsUnique();
        });

        modelBuilder.Entity<ManagedChat>(entity =>
        {
            entity.ToTable("managed_chats");
            entity.HasKey(x => x.ChatId);
            entity.Property(x => x.ChatId).ValueGeneratedNever();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.CreatedAt).HasConversion(converter).IsRequired();
            entity.Property(x => x.AuthorizedBy).HasMaxLength(64).IsRequired();
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
    }
}
