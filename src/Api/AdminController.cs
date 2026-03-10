using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;
using TelegramChainBot.Services;

namespace TelegramChainBot.Api;

public static class AdminController
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin");

        group.MapPost("/login", LoginAsync);
        group.MapPost("/change-password", ChangePasswordAsync).AddEndpointFilter(AdminAuthFilter);
        
        group.MapGet("/chains", GetChainsAsync).AddEndpointFilter(AdminAuthFilter);
        group.MapDelete("/chains/{id:long}", DeleteChainAsync).AddEndpointFilter(AdminAuthFilter);
        
        group.MapGet("/chains/{id:long}/members", GetMembersAsync).AddEndpointFilter(AdminAuthFilter);
        group.MapDelete("/members/{id:long}", DeleteMemberAsync).AddEndpointFilter(AdminAuthFilter);
        group.MapPut("/members/{id:long}", UpdateMemberAsync).AddEndpointFilter(AdminAuthFilter);

        return app;
    }

    private static async ValueTask<object?> AdminAuthFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var sessionService = httpContext.RequestServices.GetRequiredService<AdminSessionService>();
        var token = httpContext.Request.Headers["X-Admin-Token"].ToString();

        var adminId = sessionService.GetAdminId(token);
        if (adminId == null)
        {
            return Results.Unauthorized();
        }

        httpContext.Items["AdminId"] = adminId;
        return await next(context);
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, AdminService adminService, AdminSessionService sessionService, CancellationToken ct)
    {
        var admin = await adminService.LoginAsync(request.Username, request.Password, ct);
        if (admin == null) return Results.Unauthorized();

        var token = sessionService.CreateSession(admin.Id);
        return Results.Ok(new { token });
    }

    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, HttpContext context, AdminService adminService, CancellationToken ct)
    {
        var adminId = (int)context.Items["AdminId"]!;
        var success = await adminService.ChangePasswordAsync(adminId, request.OldPassword, request.NewPassword, ct);
        return success ? Results.Ok() : Results.BadRequest(new { error = "Invalid old password" });
    }

    private static async Task<IResult> GetChainsAsync(AppDbContext db, CancellationToken ct)
    {
        var chains = await db.Chains.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        return Results.Ok(chains);
    }

    private static async Task<IResult> DeleteChainAsync(long id, AppDbContext db, TelegramService tg, CancellationToken ct)
    {
        var chain = await db.Chains.FindAsync([id], ct);
        if (chain == null) return Results.NotFound();

        db.Chains.Remove(chain);
        var members = db.ChainMembers.Where(m => m.ChainId == id);
        db.ChainMembers.RemoveRange(members);
        
        await db.SaveChangesAsync(ct);
        
        // Optional: Notify Telegram that the chain is deleted (or just let it be)
        return Results.Ok();
    }

    private static async Task<IResult> GetMembersAsync(long id, AppDbContext db, CancellationToken ct)
    {
        var members = await db.ChainMembers.Where(m => m.ChainId == id).OrderBy(m => m.JoinTime).ToListAsync(ct);
        return Results.Ok(members);
    }

    private static async Task<IResult> DeleteMemberAsync(long id, AppDbContext db, ChainService chainService, TelegramService tg, CancellationToken ct)
    {
        var member = await db.ChainMembers.FindAsync([id], ct);
        if (member == null) return Results.NotFound();

        var chainId = member.ChainId;
        db.ChainMembers.Remove(member);
        await db.SaveChangesAsync(ct);

        // Update Telegram message
        var chain = await db.Chains.FindAsync([chainId], ct);
        if (chain != null)
        {
            var members = await db.ChainMembers.Where(m => m.ChainId == chainId).OrderBy(m => m.JoinTime).ToListAsync(ct);
            var messageText = ChainService.FormatChainMessage(chain.Title, members);
            await tg.EditChainMessageAsync(chain.ChatId, chain.MessageId, chainId, messageText, ct);
        }

        return Results.Ok();
    }

    private static async Task<IResult> UpdateMemberAsync(long id, UpdateMemberRequest request, AppDbContext db, ChainService chainService, TelegramService tg, CancellationToken ct)
    {
        var member = await db.ChainMembers.FindAsync([id], ct);
        if (member == null) return Results.NotFound();

        member.Username = request.Username;
        member.TelegramNickname = request.TelegramNickname;
        await db.SaveChangesAsync(ct);

        // Update Telegram message
        var chain = await db.Chains.FindAsync([member.ChainId], ct);
        if (chain != null)
        {
            var members = await db.ChainMembers.Where(m => m.ChainId == member.ChainId).OrderBy(m => m.JoinTime).ToListAsync(ct);
            var messageText = ChainService.FormatChainMessage(chain.Title, members);
            await tg.EditChainMessageAsync(chain.ChatId, chain.MessageId, member.ChainId, messageText, ct);
        }

        return Results.Ok();
    }

    public record LoginRequest(string Username, string Password);
    public record ChangePasswordRequest(string OldPassword, string NewPassword);
    public record UpdateMemberRequest(string Username, string TelegramNickname);
}
