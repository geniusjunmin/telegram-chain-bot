using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database;
using TelegramChainBot.Services;

namespace TelegramChainBot.Api;

public static class ChainController
{
    public static IEndpointRouteBuilder MapChainEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/join", JoinAsync);
        app.MapGet("/api/chains/{chainId:long}", GetChainAsync);
        return app;
    }

    private static async Task<IResult> JoinAsync(
        JoinRequest request,
        HttpRequest httpRequest,
        ChainService chainService,
        BotSecurityService security,
        TelegramService telegramService,
        CancellationToken cancellationToken)
    {
        var initData = httpRequest.Headers["X-Telegram-Init-Data"].ToString();
        if (!security.ValidateInitData(initData))
        {
            return Results.Unauthorized();
        }

        var chain = await chainService.GetChainAsync(request.ChainId, cancellationToken);
        if (chain is null)
        {
            return Results.NotFound(new { error = "chain not found" });
        }

        var (added, members) = await chainService.JoinAsync(
            request.ChainId,
            request.UserId,
            request.Username,
            cancellationToken);

        var messageText = ChainService.FormatChainMessage(chain.Title, members);
        await telegramService.EditChainMessageAsync(chain.ChatId, chain.MessageId, request.ChainId, messageText, cancellationToken);

        return Results.Ok(new
        {
            success = true,
            joined = added,
            updated_members = members.Select(x => new { x.UserId, x.Username, x.JoinTime })
        });
    }

    private static async Task<IResult> GetChainAsync(long chainId, AppDbContext db, CancellationToken cancellationToken)
    {
        var chain = await db.Chains.FirstOrDefaultAsync(c => c.Id == chainId, cancellationToken);
        if (chain is null)
        {
            return Results.NotFound(new { error = "chain not found" });
        }

        var members = await db.ChainMembers
            .Where(m => m.ChainId == chainId)
            .OrderBy(m => m.JoinTime)
            .Select(m => new { m.UserId, m.Username, m.JoinTime })
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            chain.Id,
            chain.Title,
            chain.CreatorId,
            chain.MessageId,
            chain.ChatId,
            chain.CreatedAt,
            members
        });
    }

    private sealed record JoinRequest(long ChainId, long UserId, string Username);
}
