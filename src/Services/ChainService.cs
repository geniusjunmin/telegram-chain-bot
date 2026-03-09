using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Services;

public sealed class ChainService(AppDbContext db)
{
    public async Task<long> CreateChainAsync(string title, long creatorId, CancellationToken cancellationToken)
    {
        var chain = new Chain
        {
            Title = title,
            CreatorId = creatorId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Chains.Add(chain);
        await db.SaveChangesAsync(cancellationToken);
        return chain.Id;
    }

    public async Task SetMessageInfoAsync(long chainId, long chatId, long messageId, CancellationToken cancellationToken)
    {
        var chain = await db.Chains.FirstAsync(c => c.Id == chainId, cancellationToken);
        chain.ChatId = chatId;
        chain.MessageId = messageId;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Chain?> GetChainAsync(long chainId, CancellationToken cancellationToken)
    {
        return await db.Chains.FirstOrDefaultAsync(c => c.Id == chainId, cancellationToken);
    }

    public async Task<IReadOnlyList<ChainMember>> GetMembersAsync(long chainId, CancellationToken cancellationToken)
    {
        return await db.ChainMembers
            .Where(m => m.ChainId == chainId)
            .OrderBy(m => m.JoinTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<(bool Added, IReadOnlyList<ChainMember> Members)> JoinAsync(
        long chainId,
        long userId,
        string username,
        CancellationToken cancellationToken)
    {
        var existing = await db.ChainMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ChainId == chainId && x.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            var currentMembers = await GetMembersAsync(chainId, cancellationToken);
            return (false, currentMembers);
        }

        db.ChainMembers.Add(new ChainMember
        {
            ChainId = chainId,
            UserId = userId,
            Username = string.IsNullOrWhiteSpace(username) ? $"user_{userId}" : username,
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

    public static string FormatChainMessage(string title, IReadOnlyList<ChainMember> members)
    {
        var lines = new List<string> { $"🍽 {title}", string.Empty };

        if (members.Count == 0)
        {
            lines.Add("1. ");
            return string.Join("\n", lines);
        }

        for (var i = 0; i < members.Count; i++)
        {
            lines.Add($"{i + 1}. {members[i].Username}");
        }

        return string.Join("\n", lines);
    }
}
