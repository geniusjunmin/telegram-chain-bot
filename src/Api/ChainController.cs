using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database;
using TelegramChainBot.Security;
using TelegramChainBot.Services;

namespace TelegramChainBot.Api;

public static class ChainController
{
    public static IEndpointRouteBuilder MapChainEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/chains/{publicId}", GetChainAsync);
        app.MapPost("/api/chains/{publicId}/join", JoinAsync);
        app.MapPost("/api/chains/{publicId}/leave", LeaveAsync);
        return app;
    }

    private static async Task<IResult> GetChainAsync(
        string publicId,
        HttpRequest httpRequest,
        AppDbContext db,
        ChainService chainService,
        TelegramInitDataValidator validator,
        CancellationToken cancellationToken)
    {
        var chain = await chainService.GetChainByPublicIdAsync(publicId, cancellationToken);
        if (chain is null)
        {
            return Results.NotFound(new { error = "chain not found" });
        }

        var members = await db.ChainMembers
            .Where(m => m.ChainId == chain.Id)
            .OrderBy(m => m.JoinTime)
            .Select(m => new { displayName = m.Username })
            .ToListAsync(cancellationToken);

        var hasJoined = false;
        var initData = httpRequest.Headers["X-Telegram-Init-Data"].ToString();
        if (!string.IsNullOrWhiteSpace(initData))
        {
            var validatedUser = validator.Validate(initData);
            if (validatedUser != null)
            {
                hasJoined = await db.ChainMembers.AnyAsync(
                    m => m.ChainId == chain.Id && m.UserId == validatedUser.UserId, 
                    cancellationToken);
            }
        }

        return Results.Ok(new
        {
            publicId = chain.PublicId,
            title = chain.Title,
            createdAt = chain.CreatedAt,
            hasJoined,
            members
        });
    }

    private static async Task<IResult> JoinAsync(
        string publicId,
        JoinRequest request,
        HttpRequest httpRequest,
        ChainService chainService,
        TelegramInitDataValidator validator,
        TelegramService telegramService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ChainController");
        var initData = httpRequest.Headers["X-Telegram-Init-Data"].ToString();
        
        var validatedUser = validator.Validate(initData);
        if (validatedUser == null)
        {
            logger.LogWarning("Invalid InitData received during Join.");
            return Results.Unauthorized();
        }

        var chain = await chainService.GetChainByPublicIdAsync(publicId, cancellationToken);
        if (chain is null)
        {
            logger.LogWarning("Chain {PublicId} not found during Join.", publicId);
            return Results.NotFound(new { error = "chain not found" });
        }

        var displayName = InputSanitizer.SanitizeName(request.DisplayName);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Results.BadRequest(new { error = "DisplayName is required." });
        }

        var telegramNickname = validatedUser.Username ?? validatedUser.FirstName;

        logger.LogInformation(
            "Join request accepted. PublicId: {PublicId}, UserId: {UserId}, DisplayName: {DisplayName}, TelegramNickname: {TelegramNickname}",
            publicId,
            validatedUser.UserId,
            displayName,
            telegramNickname);

        var (added, members) = await chainService.JoinAsync(
            chain.Id,
            validatedUser.UserId,
            displayName,
            telegramNickname,
            cancellationToken);

        var messageText = ChainService.FormatChainMessage(chain.Title, members);
        await telegramService.EditChainMessageAsync(chain.ChatId, chain.MessageId, chain.PublicId, messageText, cancellationToken);

        return Results.Ok(new
        {
            success = true,
            joined = added,
            updated_members = members.Select(x => new { displayName = x.Username })
        });
    }

    private static async Task<IResult> LeaveAsync(
        string publicId,
        HttpRequest httpRequest,
        ChainService chainService,
        TelegramInitDataValidator validator,
        TelegramService telegramService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ChainController");
        var initData = httpRequest.Headers["X-Telegram-Init-Data"].ToString();
        
        var validatedUser = validator.Validate(initData);
        if (validatedUser == null)
        {
            logger.LogWarning("Invalid InitData received during Leave.");
            return Results.Unauthorized();
        }

        var chain = await chainService.GetChainByPublicIdAsync(publicId, cancellationToken);
        if (chain is null)
        {
            logger.LogWarning("Chain {PublicId} not found during Leave.", publicId);
            return Results.NotFound(new { error = "chain not found" });
        }

        logger.LogInformation(
            "Leave request accepted. PublicId: {PublicId}, UserId: {UserId}",
            publicId,
            validatedUser.UserId);

        var (removed, members) = await chainService.LeaveAsync(
            chain.Id,
            validatedUser.UserId,
            cancellationToken);

        if (removed)
        {
            var messageText = ChainService.FormatChainMessage(chain.Title, members);
            await telegramService.EditChainMessageAsync(chain.ChatId, chain.MessageId, chain.PublicId, messageText, cancellationToken);
        }

        return Results.Ok(new
        {
            success = true,
            left = removed,
            updated_members = members.Select(x => new { displayName = x.Username })
        });
    }

    public sealed record JoinRequest(string DisplayName);
}
