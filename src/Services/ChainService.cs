using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Services;

public class ChainService(AppDbContext db)
{
    public virtual async Task<Chain> CreateChainAsync(string title, long creatorId, CancellationToken cancellationToken)
    {
        var chain = new Chain
        {
            PublicId = Guid.NewGuid().ToString("N"),
            Title = title,
            CreatorId = creatorId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Chains.Add(chain);
        await db.SaveChangesAsync(cancellationToken);
        return chain;
    }

    public virtual async Task SetMessageInfoAsync(long chainId, long chatId, long messageId, CancellationToken cancellationToken)
    {
        var chain = await db.Chains.FirstAsync(c => c.Id == chainId, cancellationToken);
        chain.ChatId = chatId;
        chain.MessageId = messageId;
        await db.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task DeleteChainAsync(long chainId, CancellationToken cancellationToken)
    {
        var chain = await db.Chains.FirstOrDefaultAsync(c => c.Id == chainId, cancellationToken);
        if (chain is null)
        {
            return;
        }

        db.Chains.Remove(chain);
        await db.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task<Chain?> GetChainAsync(long chainId, CancellationToken cancellationToken)
    {
        return await db.Chains.FirstOrDefaultAsync(c => c.Id == chainId, cancellationToken);
    }

    public virtual async Task<Chain?> GetChainByPublicIdAsync(string publicId, CancellationToken cancellationToken)
    {
        return await db.Chains.FirstOrDefaultAsync(c => c.PublicId == publicId, cancellationToken);
    }

    public virtual async Task<IReadOnlyList<ChainMember>> GetMembersAsync(long chainId, CancellationToken cancellationToken)
    {
        return await db.ChainMembers
            .Where(m => m.ChainId == chainId)
            .OrderBy(m => m.JoinTime)
            .ToListAsync(cancellationToken);
    }

    public virtual async Task<(bool Added, IReadOnlyList<ChainMember> Members)> JoinAsync(
        long chainId,
        long userId,
        string username,
        string telegramNickname,
        CancellationToken cancellationToken)
    {
        var existing = await db.ChainMembers
            .FirstOrDefaultAsync(x => x.ChainId == chainId && x.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            var normalizedUsername = string.IsNullOrWhiteSpace(username) ? $"user_{userId}" : username;
            var normalizedTelegramNickname = string.IsNullOrWhiteSpace(telegramNickname) ? normalizedUsername : telegramNickname;
            if (!string.Equals(existing.Username, normalizedUsername, StringComparison.Ordinal) ||
                !string.Equals(existing.TelegramNickname, normalizedTelegramNickname, StringComparison.Ordinal))
            {
                existing.Username = normalizedUsername;
                existing.TelegramNickname = normalizedTelegramNickname;
                await db.SaveChangesAsync(cancellationToken);
            }

            var currentMembers = await GetMembersAsync(chainId, cancellationToken);
            return (false, currentMembers);
        }

        db.ChainMembers.Add(new ChainMember
        {
            ChainId = chainId,
            UserId = userId,
            Username = string.IsNullOrWhiteSpace(username) ? $"user_{userId}" : username,
            TelegramNickname = string.IsNullOrWhiteSpace(telegramNickname)
                ? (string.IsNullOrWhiteSpace(username) ? $"user_{userId}" : username)
                : telegramNickname,
            JoinTime = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Handles concurrent join race by falling back to current list.
        }

        var members = await GetMembersAsync(chainId, cancellationToken);
        var added = members.Any(m => m.UserId == userId);
        return (added, members);
    }

    public virtual async Task<(bool Removed, IReadOnlyList<ChainMember> Members)> LeaveAsync(
        long chainId,
        long userId,
        CancellationToken cancellationToken)
    {
        var existing = await db.ChainMembers
            .FirstOrDefaultAsync(x => x.ChainId == chainId && x.UserId == userId, cancellationToken);

        if (existing is null)
        {
            var currentMembers = await GetMembersAsync(chainId, cancellationToken);
            return (false, currentMembers);
        }

        db.ChainMembers.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);

        var members = await GetMembersAsync(chainId, cancellationToken);
        return (true, members);
    }

    public static string FormatChainMessage(string title, IReadOnlyList<ChainMember> members)
    {
        return TelegramMessageFormatter.FormatChainMessage(title, members);
    }
}
