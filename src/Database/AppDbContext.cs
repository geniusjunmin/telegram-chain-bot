using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Database;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Chain> Chains => Set<Chain>();
    public DbSet<ChainMember> ChainMembers => Set<ChainMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Chain>(entity =>
        {
            entity.ToTable("chains");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<ChainMember>(entity =>
        {
            entity.ToTable("chain_members");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(128).IsRequired();
            entity.Property(x => x.JoinTime).IsRequired();
            entity.HasIndex(x => new { x.ChainId, x.UserId }).IsUnique();
        });
    }
}
