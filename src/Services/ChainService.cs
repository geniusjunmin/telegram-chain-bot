using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;

namespace TelegramChainBot.Services;

public class ChainService(AppDbContext db)
{
    public virtual async Task<Chain> CreateChainAsync(string title, long creatorTelegramUserId, CancellationToken cancellationToken)
    {
        var chain = new Chain
        {
            PublicId = Guid.NewGuid().ToString("N"),
            Title = title,
            CreatorTelegramUserId = creatorTelegramUserId,
            Status = ChainStatus.Creating,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TelegramSyncStatus = TelegramSyncStatus.Pending
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
        chain.Status = ChainStatus.Active;
        chain.TelegramSyncStatus = TelegramSyncStatus.Synced;
        chain.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task DeleteChainAsync(long chainId, CancellationToken cancellationToken)
    {
        var chain = await db.Chains.FirstOrDefaultAsync(c => c.Id == chainId, cancellationToken);
        if (chain is null) return;

        chain.Status = ChainStatus.Deleted;
        chain.DeletedAt = DateTimeOffset.UtcNow;
        chain.UpdatedAt = DateTimeOffset.UtcNow;
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
            .Where(m => m.ChainId == chainId && m.Status == ChainMemberStatus.Active)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(cancellationToken);
    }

    public virtual async Task<(bool Added, IReadOnlyList<ChainMember> Members, string? Error)> JoinAsync(
        long chainId,
        long telegramUserId,
        string username,
        string telegramNickname,
        CancellationToken cancellationToken)
    {
        var chain = await db.Chains.FirstOrDefaultAsync(c => c.Id == chainId, cancellationToken);
        if (chain == null) return (false, Array.Empty<ChainMember>(), "Chain not found");

        if (chain.Status == ChainStatus.Closed || chain.Status == ChainStatus.Expired || chain.Status == ChainStatus.Deleted || chain.Status == ChainStatus.Cancelled)
        {
            return (false, await GetMembersAsync(chainId, cancellationToken), "Chain is not active");
        }

        var activeMembers = await GetMembersAsync(chainId, cancellationToken);
        if (activeMembers.Count >= chain.MaxMembers)
        {
            return (false, activeMembers, "Chain is full");
        }

        var normalizedUsername = string.IsNullOrWhiteSpace(username) ? $"user_{telegramUserId}" : username;
        var normalizedTelegramNickname = string.IsNullOrWhiteSpace(telegramNickname) ? normalizedUsername : telegramNickname;

        var existing = await db.ChainMembers
            .FirstOrDefaultAsync(x => x.ChainId == chainId && x.TelegramUserId == telegramUserId, cancellationToken);

        if (existing is not null)
        {
            if (existing.Status == ChainMemberStatus.Active)
            {
                if (!string.Equals(existing.DisplayName, normalizedUsername, StringComparison.Ordinal) ||
                    !string.Equals(existing.TelegramUsername, normalizedTelegramNickname, StringComparison.Ordinal))
                {
                    existing.DisplayName = normalizedUsername;
                    existing.TelegramUsername = normalizedTelegramNickname;
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                }
                return (false, await GetMembersAsync(chainId, cancellationToken), null);
            }
            else
            {
                existing.Status = ChainMemberStatus.Active;
                existing.DisplayName = normalizedUsername;
                existing.TelegramUsername = normalizedTelegramNickname;
                existing.JoinedAt = DateTimeOffset.UtcNow;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                existing.LeftAt = null;
                existing.RemovedAt = null;
                await db.SaveChangesAsync(cancellationToken);
                return (true, await GetMembersAsync(chainId, cancellationToken), null);
            }
        }

        db.ChainMembers.Add(new ChainMember
        {
            ChainId = chainId,
            TelegramUserId = telegramUserId,
            DisplayName = normalizedUsername,
            TelegramUsername = normalizedTelegramNickname,
            Status = ChainMemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
        }

        var members = await GetMembersAsync(chainId, cancellationToken);
        var added = members.Any(m => m.TelegramUserId == telegramUserId);
        return (added, members, null);
    }

    public virtual async Task<(bool Removed, IReadOnlyList<ChainMember> Members, string? Error)> LeaveAsync(
        long chainId,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var chain = await db.Chains.FirstOrDefaultAsync(c => c.Id == chainId, cancellationToken);
        if (chain == null) return (false, Array.Empty<ChainMember>(), "Chain not found");

        if (chain.Status == ChainStatus.Closed || chain.Status == ChainStatus.Expired || chain.Status == ChainStatus.Deleted || chain.Status == ChainStatus.Cancelled)
        {
            return (false, await GetMembersAsync(chainId, cancellationToken), "Chain is not active");
        }

        var existing = await db.ChainMembers
            .FirstOrDefaultAsync(x => x.ChainId == chainId && x.TelegramUserId == telegramUserId, cancellationToken);

        if (existing is null || existing.Status != ChainMemberStatus.Active)
        {
            return (false, await GetMembersAsync(chainId, cancellationToken), null);
        }

        existing.Status = ChainMemberStatus.Left;
        existing.LeftAt = DateTimeOffset.UtcNow;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var members = await GetMembersAsync(chainId, cancellationToken);
        return (true, members, null);
    }

    public static string FormatChainMessage(string title, IReadOnlyList<ChainMember> members)
    {
        return TelegramMessageFormatter.FormatChainMessage(title, members);
    }
}
