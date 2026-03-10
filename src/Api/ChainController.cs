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
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ChainController");
        var initData = httpRequest.Headers["X-Telegram-Init-Data"].ToString();
        if (!security.ValidateInitData(initData))
        {
            logger.LogWarning("Invalid InitData received.");
            return Results.Unauthorized();
        }

        var chain = await chainService.GetChainAsync(request.ChainId, cancellationToken);
        if (chain is null)
        {
            logger.LogWarning("Chain {ChainId} not found during Join.", request.ChainId);
            return Results.NotFound(new { error = "chain not found" });
        }
        // ...

        var displayName = NormalizeDisplayName(request.Username, request.UserId);
        var telegramNickname = NormalizeDisplayName(request.TelegramNickname, request.UserId);
        logger.LogInformation(
            "Join request accepted. ChainId: {ChainId}, UserId: {UserId}, DisplayName: {DisplayName}, TelegramNickname: {TelegramNickname}",
            request.ChainId,
            request.UserId,
            displayName,
            telegramNickname);

        var (added, members) = await chainService.JoinAsync(
            request.ChainId,
            request.UserId,
            displayName,
            telegramNickname,
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

    private static async Task<IResult> GetChainAsync(long chainId, AppDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ChainController");
        var chain = await db.Chains.FirstOrDefaultAsync(c => c.Id == chainId, cancellationToken);
        if (chain is null)
        {
            logger.LogWarning("Chain {ChainId} not found during Get.", chainId);
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

    private static string NormalizeDisplayName(string username, long userId)
    {
        var trimmed = username.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return $"user_{userId}";
        }

        return trimmed.Length <= 32 ? trimmed : trimmed[..32];
    }

    public sealed record JoinRequest(long ChainId, long UserId, string Username, string TelegramNickname);
}
