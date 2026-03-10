using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database.Models;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TelegramChainBot.Database;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Chain> Chains => Set<Chain>();
    public DbSet<ChainMember> ChainMembers => Set<ChainMember>();
    public DbSet<Admin> Admins => Set<Admin>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var converter = new DateTimeOffsetToStringConverter();

        modelBuilder.Entity<Admin>(entity =>
        {
            entity.ToTable("admins");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<Chain>(entity =>
        {
            entity.ToTable("chains");
            entity.HasKey(x => x.Id);
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
    }
}
