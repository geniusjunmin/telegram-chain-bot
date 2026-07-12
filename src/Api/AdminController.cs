using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;
using TelegramChainBot.Security;
using TelegramChainBot.Services;

namespace TelegramChainBot.Api;

public static class AdminController
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin");

        group.MapGet("/csrf", GetCsrfToken).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("login-limiter");
        group.MapPost("/logout", LogoutAsync).RequireAuthorization().AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/change-password", ChangePasswordAsync).RequireAuthorization("Admin.ManageSettings").AddEndpointFilter(AntiforgeryFilter);
        
        group.MapGet("/chains", GetChainsAsync).RequireAuthorization("Admin.Read");
        group.MapDelete("/chains/{id:long}", DeleteChainAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);
        
        group.MapGet("/chains/{id:long}/members", GetMembersAsync).RequireAuthorization("Admin.Read");
        group.MapDelete("/members/{id:long}", DeleteMemberAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);
        group.MapPut("/members/{id:long}", UpdateMemberAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);

        // RootAdmin management endpoints
        group.MapGet("/accounts", GetAccountsAsync).RequireAuthorization("Admin.ManageAccounts");
        group.MapPost("/accounts", CreateAccountAsync).RequireAuthorization("Admin.ManageAccounts").AddEndpointFilter(AntiforgeryFilter);
        group.MapDelete("/accounts/{id:int}", DeleteAccountAsync).RequireAuthorization("Admin.ManageAccounts").AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/accounts/{id:int}/reset-password", ResetPasswordAsync).RequireAuthorization("Admin.ManageAccounts").AddEndpointFilter(AntiforgeryFilter);

        // Audit log endpoints
        group.MapGet("/audit-logs", GetAuditLogsAsync).RequireAuthorization("Admin.Read");

        return app;
    }

    private static async ValueTask<object?> AntiforgeryFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { error = "Invalid CSRF token" });
        }
        return await next(context);
    }

    private static IResult GetCsrfToken(IAntiforgery antiforgery, HttpContext context)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, // Must be readable by client JS
            Secure = true,    // Production secure
            SameSite = SameSiteMode.Strict
        });
        return Results.Ok();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, 
        HttpContext context,
        AdminService adminService, 
        AdminSessionService sessionService, 
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var admin = await adminService.LoginAsync(request.Username, request.Password, ct);
        if (admin == null)
        {
            await auditLog.LogAsync("Login", "AdminAccount", request.Username, success: false, failureReason: "Invalid credentials", actorType: "Admin");
            return Results.Unauthorized();
        }

        var sessionId = sessionService.RegisterSession(admin.Id);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, admin.Username),
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new Claim(ClaimTypes.Role, admin.Role.ToString()),
            new Claim("SessionId", sessionId)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        });

        await auditLog.LogAsync("Login", "AdminAccount", admin.Id.ToString(), success: true, actorType: "Admin", overrideActorAdminId: admin.Id);

        return Results.Ok(new { success = true, role = admin.Role.ToString() });
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context, 
        AdminSessionService sessionService,
        AuditLogService auditLog)
    {
        var sessionIdClaim = context.User.FindFirst("SessionId")?.Value;
        sessionService.RevokeSession(sessionIdClaim);

        var adminIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(adminIdClaim, out var adminId))
        {
            await auditLog.LogAsync("Logout", "AdminAccount", adminId.ToString(), success: true, actorType: "Admin");
        }

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request, 
        HttpContext context, 
        AdminService adminService, 
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var adminIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(adminIdClaim, out var adminId))
        {
            return Results.Unauthorized();
        }

        var success = await adminService.ChangePasswordAsync(adminId, request.OldPassword, request.NewPassword, ct);
        if (success)
        {
            await auditLog.LogAsync("ChangePassword", "AdminAccount", adminId.ToString(), success: true, actorType: "Admin");
            return Results.Ok();
        }
        else
        {
            await auditLog.LogAsync("ChangePassword", "AdminAccount", adminId.ToString(), success: false, failureReason: "Invalid old password", actorType: "Admin");
            return Results.BadRequest(new { error = "Invalid old password" });
        }
    }

    private static async Task<IResult> GetChainsAsync(AppDbContext db, CancellationToken ct)
    {
        var chains = await db.Chains.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        return Results.Ok(chains);
    }

    private static async Task<IResult> DeleteChainAsync(
        long id, 
        AppDbContext db, 
        TelegramService tg, 
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var chain = await db.Chains.FindAsync([id], ct);
        if (chain == null) return Results.NotFound();

        db.Chains.Remove(chain);
        var members = db.ChainMembers.Where(m => m.ChainId == id);
        db.ChainMembers.RemoveRange(members);
        
        await db.SaveChangesAsync(ct);

        await auditLog.LogAsync("DeleteChain", "Chain", id.ToString(), chatId: chain.ChatId, success: true, actorType: "Admin");
        
        return Results.Ok();
    }

    private static async Task<IResult> GetMembersAsync(long id, AppDbContext db, CancellationToken ct)
    {
        var members = await db.ChainMembers.Where(m => m.ChainId == id).OrderBy(m => m.JoinTime).ToListAsync(ct);
        return Results.Ok(members);
    }

    private static async Task<IResult> DeleteMemberAsync(
        long id, 
        AppDbContext db, 
        ChainService chainService, 
        TelegramService tg, 
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var member = await db.ChainMembers.FindAsync([id], ct);
        if (member == null) return Results.NotFound();

        var chainId = member.ChainId;
        db.ChainMembers.Remove(member);
        await db.SaveChangesAsync(ct);

        var chain = await db.Chains.FindAsync([chainId], ct);
        if (chain != null)
        {
            var members = await db.ChainMembers.Where(m => m.ChainId == chainId).OrderBy(m => m.JoinTime).ToListAsync(ct);
            var messageText = ChainService.FormatChainMessage(chain.Title, members);
            await tg.EditChainMessageAsync(chain.ChatId, chain.MessageId, chain.PublicId, messageText, ct);
        }

        await auditLog.LogAsync("DeleteMember", "ChainMember", id.ToString(), chatId: chain?.ChatId, success: true, actorType: "Admin");

        return Results.Ok();
    }

    private static async Task<IResult> UpdateMemberAsync(
        long id, 
        UpdateMemberRequest request, 
        AppDbContext db, 
        ChainService chainService, 
        TelegramService tg, 
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var member = await db.ChainMembers.FindAsync([id], ct);
        if (member == null) return Results.NotFound();

        var beforeJson = System.Text.Json.JsonSerializer.Serialize(new { member.Username, member.TelegramNickname });

        member.Username = request.Username;
        member.TelegramNickname = request.TelegramNickname;
        await db.SaveChangesAsync(ct);

        var chain = await db.Chains.FindAsync([member.ChainId], ct);
        if (chain != null)
        {
            var members = await db.ChainMembers.Where(m => m.ChainId == member.ChainId).OrderBy(m => m.JoinTime).ToListAsync(ct);
            var messageText = ChainService.FormatChainMessage(chain.Title, members);
            await tg.EditChainMessageAsync(chain.ChatId, chain.MessageId, chain.PublicId, messageText, ct);
        }

        var afterJson = System.Text.Json.JsonSerializer.Serialize(new { member.Username, member.TelegramNickname });
        await auditLog.LogAsync("UpdateMember", "ChainMember", id.ToString(), chatId: chain?.ChatId, beforeJson: beforeJson, afterJson: afterJson, success: true, actorType: "Admin");

        return Results.Ok();
    }

    private static async Task<IResult> GetAccountsAsync(AppDbContext db, CancellationToken ct)
    {
        var accounts = await db.AdminAccounts.OrderBy(a => a.Username).ToListAsync(ct);
        return Results.Ok(accounts.Select(a => new { a.Id, a.Username, Role = a.Role.ToString(), a.IsDisabled }));
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateAccountRequest request,
        AppDbContext db,
        IPasswordHasher<AdminAccount> hasher,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        if (await db.AdminAccounts.AnyAsync(a => a.Username == request.Username, ct))
        {
            return Results.BadRequest(new { error = "Username already exists" });
        }

        if (!Enum.TryParse<AdminRole>(request.Role, true, out var role))
        {
            return Results.BadRequest(new { error = "Invalid role" });
        }

        var account = new AdminAccount
        {
            Username = request.Username,
            Role = role,
            IsDisabled = false,
            PasswordHash = string.Empty
        };
        account.PasswordHash = hasher.HashPassword(account, request.Password);

        db.AdminAccounts.Add(account);
        await db.SaveChangesAsync(ct);

        await auditLog.LogAsync("CreateAdminAccount", "AdminAccount", account.Id.ToString(), success: true, actorType: "Admin");

        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> DeleteAccountAsync(
        int id,
        HttpContext context,
        AppDbContext db,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var currentAdminIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(currentAdminIdClaim, out var currentAdminId) && currentAdminId == id)
        {
            return Results.BadRequest(new { error = "Cannot delete current admin account" });
        }

        var account = await db.AdminAccounts.FindAsync([id], ct);
        if (account == null) return Results.NotFound();

        if (account.Role == AdminRole.RootAdmin)
        {
            return Results.BadRequest(new { error = "Cannot delete RootAdmin account" });
        }

        db.AdminAccounts.Remove(account);
        await db.SaveChangesAsync(ct);

        await auditLog.LogAsync("DeleteAdminAccount", "AdminAccount", id.ToString(), success: true, actorType: "Admin");

        return Results.Ok();
    }

    private static async Task<IResult> ResetPasswordAsync(
        int id,
        ResetPasswordRequest request,
        AppDbContext db,
        IPasswordHasher<AdminAccount> hasher,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var account = await db.AdminAccounts.FindAsync([id], ct);
        if (account == null) return Results.NotFound();

        account.PasswordHash = hasher.HashPassword(account, request.Password);
        await db.SaveChangesAsync(ct);

        await auditLog.LogAsync("ResetAdminPassword", "AdminAccount", id.ToString(), success: true, actorType: "Admin");

        return Results.Ok();
    }

    private static async Task<IResult> GetAuditLogsAsync(
        string? actor,
        string? action,
        string? entityType,
        long? chatId,
        bool? success,
        int? page,
        int? pageSize,
        AppDbContext db,
        CancellationToken ct)
    {
        var query = db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(actor))
        {
            if (int.TryParse(actor, out var adminId))
            {
                query = query.Where(l => l.ActorAdminId == adminId);
            }
            else
            {
                var matchingAdminIds = await db.AdminAccounts
                    .Where(a => a.Username.Contains(actor))
                    .Select(a => a.Id)
                    .ToListAsync(ct);
                query = query.Where(l => l.ActorAdminId != null && matchingAdminIds.Contains(l.ActorAdminId.Value));
            }
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(l => l.Action.Contains(action));
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(l => l.EntityType.Contains(entityType));
        }

        if (chatId.HasValue)
        {
            query = query.Where(l => l.ChatId == chatId.Value);
        }

        if (success.HasValue)
        {
            query = query.Where(l => l.Success == success.Value);
        }

        int pageNum = page ?? 1;
        int size = pageSize ?? 10;

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return Results.Ok(new
        {
            items = items.Select(l => new
            {
                l.Id,
                l.ActorType,
                l.ActorAdminId,
                l.ActorTelegramUserId,
                l.Action,
                l.EntityType,
                l.EntityId,
                l.ChatId,
                l.IpAddressHash,
                l.UserAgent,
                l.CreatedAt,
                l.CorrelationId,
                l.Success,
                l.FailureReason
            }),
            total,
            page = pageNum,
            pageSize = size
        });
    }

    public record LoginRequest(string Username, string Password);
    public record ChangePasswordRequest(string OldPassword, string NewPassword);
    public record UpdateMemberRequest(string Username, string TelegramNickname);
    public record CreateAccountRequest(string Username, string Password, string Role);
    public record ResetPasswordRequest(string Password);
}
