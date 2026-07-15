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
using TelegramChainBot.Services;

namespace TelegramChainBot.Api;

public static class AdminController
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .AddEndpointFilter(MustChangePasswordFilter);

        group.MapGet("/csrf", GetCsrfToken).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("login-limiter");
        group.MapPost("/logout", LogoutAsync).RequireAuthorization().AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/change-password", ChangePasswordAsync).RequireAuthorization("Admin.ManageSettings").AddEndpointFilter(AntiforgeryFilter);
        group.MapGet("/auth/me", GetCurrentAdminAsync).RequireAuthorization();

        group.MapGet("/chains", GetChainsAsync).RequireAuthorization("Admin.Read");
        group.MapDelete("/chains/{publicId}", DeleteChainByPublicIdAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/chains/{publicId}/close", CloseChainAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/chains/{publicId}/restore", RestoreChainAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/chains/{publicId}/cancel", CancelChainAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/chains/{publicId}/resync", ResyncChainAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);

        group.MapGet("/chains/{id:long}/members", GetMembersAsync).RequireAuthorization("Admin.Read");
        group.MapDelete("/members/{id:long}", DeleteMemberAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);
        group.MapPut("/members/{id:long}", UpdateMemberAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);

        // Chats management endpoints
        group.MapGet("/chats", GetChatsAsync).RequireAuthorization("Admin.Read");
        group.MapPost("/chats/{id:long}/approve", ApproveChatAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/chats/{id:long}/block", BlockChatAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);
        group.MapPut("/chats/{id:long}", UpdateChatAsync).RequireAuthorization("Admin.ManageChains").AddEndpointFilter(AntiforgeryFilter);

        // RootAdmin management endpoints
        group.MapGet("/accounts", GetAccountsAsync).RequireAuthorization("Admin.ManageAccounts");
        group.MapPost("/accounts", CreateAccountAsync).RequireAuthorization("Admin.ManageAccounts").AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/accounts/{id:int}/disable", DisableAccountAsync).RequireAuthorization("Admin.ManageAccounts").AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/accounts/{id:int}/enable", EnableAccountAsync).RequireAuthorization("Admin.ManageAccounts").AddEndpointFilter(AntiforgeryFilter);
        group.MapPut("/accounts/{id:int}", UpdateAccountAsync).RequireAuthorization("Admin.ManageAccounts").AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/accounts/{id:int}/reset-password", ResetPasswordAsync).RequireAuthorization("Admin.ManageAccounts").AddEndpointFilter(AntiforgeryFilter);
        group.MapPost("/accounts/{id:int}/revoke-sessions", RevokeSessionsAsync).RequireAuthorization("Admin.ManageAccounts").AddEndpointFilter(AntiforgeryFilter);

        // Audit log endpoints
        group.MapGet("/audit-logs", GetAuditLogsAsync).RequireAuthorization("Admin.Read");

        // Dashboard stats and settings endpoints
        group.MapGet("/dashboard-stats", GetDashboardStatsAsync).RequireAuthorization("Admin.Read");
        group.MapGet("/system-settings", GetSystemSettingsAsync).RequireAuthorization("Admin.Read");
        group.MapPost("/system-settings", UpdateSystemSettingsAsync).RequireAuthorization("Admin.ManageSettings").AddEndpointFilter(AntiforgeryFilter);

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
            HttpOnly = false,
            Secure = true,
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
        try
        {
            var admin = await adminService.LoginAsync(request.Username, request.Password, ct);
            if (admin == null)
            {
                await auditLog.LogAsync("Login", "AdminAccount", request.Username, success: false, failureReason: "Invalid credentials", actorType: "Admin");
                return Results.Unauthorized();
            }

            var cookieHoursEnv = Environment.GetEnvironmentVariable("ADMIN_COOKIE_HOURS");
            if (!int.TryParse(cookieHoursEnv, out var cookieHours))
            {
                cookieHours = 8;
            }
            var expiry = TimeSpan.FromHours(cookieHours);

            var sessionId = await sessionService.RegisterSessionAsync(admin.Id, expiry, ct);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, admin.Username),
                new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                new Claim(ClaimTypes.Role, admin.Role.ToString()),
                new Claim("SessionId", sessionId),
                new Claim("SecurityStamp", admin.SecurityStamp),
                new Claim("MustChangePassword", admin.MustChangePassword.ToString())
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(expiry)
            });

            await auditLog.LogAsync("Login", "AdminAccount", admin.Id.ToString(), success: true, actorType: "Admin", overrideActorAdminId: admin.Id);

            return Results.Ok(new { success = true, role = admin.Role.ToString() });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LOGIN EXCEPTION: {ex}");
            throw;
        }
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        AdminSessionService sessionService,
        AuditLogService auditLog)
    {
        var sessionIdClaim = context.User.FindFirst("SessionId")?.Value;
        await sessionService.RevokeSessionAsync(sessionIdClaim, context.RequestAborted);

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
        AdminSessionService sessionService,
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
            await sessionService.RevokeAllSessionsForAdminAsync(adminId, ct);
            await auditLog.LogAsync("ChangePassword", "AdminAccount", adminId.ToString(), success: true, actorType: "Admin");
            return Results.Ok();
        }
        else
        {
            await auditLog.LogAsync("ChangePassword", "AdminAccount", adminId.ToString(), success: false, failureReason: "Invalid old password", actorType: "Admin");
            return Results.BadRequest(new { error = "Invalid old password" });
        }
    }

    private static async Task<IResult> GetCurrentAdminAsync(
        HttpContext context,
        AppDbContext db,
        CancellationToken ct)
    {
        var adminIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(adminIdClaim, out var adminId))
        {
            return Results.Unauthorized();
        }

        var admin = await db.AdminAccounts.FindAsync([adminId], ct);
        if (admin == null || !admin.IsActive)
        {
            return Results.Unauthorized();
        }

        var permissions = new List<string> { "Admin.Read" };
        if (admin.Role == AdminRole.RootAdmin || admin.Role == AdminRole.OperatorAdmin)
        {
            permissions.Add("Admin.ManageChains");
            permissions.Add("Admin.ManageSettings");
        }
        if (admin.Role == AdminRole.RootAdmin)
        {
            permissions.Add("Admin.ManageAccounts");
        }

        return Results.Ok(new
        {
            id = admin.Id,
            username = admin.Username,
            role = admin.Role.ToString(),
            permissions = permissions,
            mustChangePassword = admin.MustChangePassword
        });
    }

    private static async Task<IResult> GetChainsAsync(
        AppDbContext db,
        string? search,
        string? status,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = db.Chains.Where(c => c.Status != ChainStatus.Deleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            query = query.Where(c => c.Title.ToLower().Contains(searchLower));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ChainStatus>(status, true, out var filterStatus))
        {
            query = query.Where(c => c.Status == filterStatus);
        }

        var total = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling((double)total / pageSize);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.PublicId,
                c.ChatId,
                c.Title,
                Status = c.Status.ToString(),
                c.MaxMembers,
                c.ExpiresAt,
                c.CreatedAt,
                c.UpdatedAt,
                c.ClosedAt,
                TelegramSyncStatus = c.TelegramSyncStatus.ToString(),
                c.LastSyncError,
                c.LastSyncedAt
            })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            items,
            page,
            pageSize,
            total,
            totalPages
        });
    }

    private static async Task<IResult> GetChatsAsync(
        AppDbContext db,
        string? search,
        string? status,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = db.ManagedChats.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            query = query.Where(c => c.Title.ToLower().Contains(searchLower));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AuthorizationStatus>(status, true, out var authStatus))
        {
            query = query.Where(c => c.AuthorizationStatus == authStatus);
        }

        var total = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling((double)total / pageSize);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.ChatId,
                c.Title,
                c.ChatType,
                AuthorizationStatus = c.AuthorizationStatus.ToString(),
                CreatePolicy = c.CreatePolicy.ToString(),
                c.MaxActiveChains,
                c.CreatedAt,
                c.UpdatedAt
            })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            items,
            page,
            pageSize,
            total,
            totalPages
        });
    }

    private static async Task<IResult> ApproveChatAsync(
        long id,
        HttpContext context,
        AppDbContext db,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var adminIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(adminIdClaim, out var adminId);

        var chat = await db.ManagedChats.FindAsync([id], ct);
        if (chat == null) return Results.NotFound();

        var before = System.Text.Json.JsonSerializer.Serialize(chat);
        chat.AuthorizationStatus = AuthorizationStatus.Approved;
        chat.ApprovedAt = DateTimeOffset.UtcNow;
        chat.ApprovedByAdminId = adminId;
        chat.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var after = System.Text.Json.JsonSerializer.Serialize(chat);
        await auditLog.LogAsync("ApproveChat", "ManagedChat", id.ToString(), success: true, actorType: "Admin", beforeJson: before, afterJson: after);

        return Results.Ok(chat);
    }

    private static async Task<IResult> BlockChatAsync(
        long id,
        HttpContext context,
        AppDbContext db,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var adminIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(adminIdClaim, out var adminId);

        var chat = await db.ManagedChats.FindAsync([id], ct);
        if (chat == null) return Results.NotFound();

        var before = System.Text.Json.JsonSerializer.Serialize(chat);
        chat.AuthorizationStatus = AuthorizationStatus.Blocked;
        chat.BlockedAt = DateTimeOffset.UtcNow;
        chat.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var after = System.Text.Json.JsonSerializer.Serialize(chat);
        await auditLog.LogAsync("BlockChat", "ManagedChat", id.ToString(), success: true, actorType: "Admin", beforeJson: before, afterJson: after);

        return Results.Ok(chat);
    }

    public record UpdateChatRequest(
        bool IsJoinEnabled,
        int DefaultMaxMembers,
        int MaxActiveChains,
        CreatePolicy CreatePolicy);

    private static async Task<IResult> UpdateChatAsync(
        long id,
        UpdateChatRequest request,
        HttpContext context,
        AppDbContext db,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var chat = await db.ManagedChats.FindAsync([id], ct);
        if (chat == null) return Results.NotFound();

        var before = System.Text.Json.JsonSerializer.Serialize(chat);
        chat.IsJoinEnabled = request.IsJoinEnabled;
        chat.DefaultMaxMembers = request.DefaultMaxMembers;
        chat.MaxActiveChains = request.MaxActiveChains;
        chat.CreatePolicy = request.CreatePolicy;
        chat.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var after = System.Text.Json.JsonSerializer.Serialize(chat);
        await auditLog.LogAsync("UpdateChat", "ManagedChat", id.ToString(), success: true, actorType: "Admin", beforeJson: before, afterJson: after);

        return Results.Ok(chat);
    }

    private static async Task<IResult> DeleteChainByPublicIdAsync(
        string publicId,
        AppDbContext db,
        TelegramService tg,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var chain = await db.Chains.FirstOrDefaultAsync(c => c.PublicId == publicId, ct);
        if (chain == null) return Results.NotFound();

        chain.Status = ChainStatus.Deleted;
        chain.DeletedAt = DateTimeOffset.UtcNow;
        chain.UpdatedAt = DateTimeOffset.UtcNow;

        // Soft-delete active members of this chain
        var activeMembers = await db.ChainMembers
            .Where(m => m.ChainId == chain.Id && m.Status == ChainMemberStatus.Active)
            .ToListAsync(ct);
        foreach (var m in activeMembers)
        {
            m.Status = ChainMemberStatus.Removed;
            m.RemovedAt = DateTimeOffset.UtcNow;
            m.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        await tg.SyncChainMessageAsync(chain.Id, ct);

        await auditLog.LogAsync("DeleteChain", "Chain", chain.Id.ToString(), chatId: chain.ChatId, success: true, actorType: "Admin");

        return Results.Ok();
    }

    private static async Task<IResult> CloseChainAsync(
        string publicId,
        AppDbContext db,
        TelegramService tg,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var chain = await db.Chains.FirstOrDefaultAsync(c => c.PublicId == publicId, ct);
        if (chain == null) return Results.NotFound();

        chain.Status = ChainStatus.Closed;
        chain.ClosedAt = DateTimeOffset.UtcNow;
        chain.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await tg.SyncChainMessageAsync(chain.Id, ct);
        await auditLog.LogAsync("CloseChain", "Chain", chain.Id.ToString(), chatId: chain.ChatId, success: true, actorType: "Admin");

        return Results.Ok();
    }

    private static async Task<IResult> RestoreChainAsync(
        string publicId,
        AppDbContext db,
        TelegramService tg,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var chain = await db.Chains.FirstOrDefaultAsync(c => c.PublicId == publicId, ct);
        if (chain == null) return Results.NotFound();

        chain.Status = ChainStatus.Active;
        chain.ClosedAt = null;
        chain.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await tg.SyncChainMessageAsync(chain.Id, ct);
        await auditLog.LogAsync("RestoreChain", "Chain", chain.Id.ToString(), chatId: chain.ChatId, success: true, actorType: "Admin");

        return Results.Ok();
    }

    private static async Task<IResult> CancelChainAsync(
        string publicId,
        AppDbContext db,
        TelegramService tg,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var chain = await db.Chains.FirstOrDefaultAsync(c => c.PublicId == publicId, ct);
        if (chain == null) return Results.NotFound();

        chain.Status = ChainStatus.Cancelled;
        chain.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await tg.SyncChainMessageAsync(chain.Id, ct);
        await auditLog.LogAsync("CancelChain", "Chain", chain.Id.ToString(), chatId: chain.ChatId, success: true, actorType: "Admin");

        return Results.Ok();
    }

    private static async Task<IResult> ResyncChainAsync(
        string publicId,
        AppDbContext db,
        TelegramService tg,
        CancellationToken ct)
    {
        var chain = await db.Chains.FirstOrDefaultAsync(c => c.PublicId == publicId, ct);
        if (chain == null) return Results.NotFound();

        await tg.SyncChainMessageAsync(chain.Id, ct);

        return Results.Ok();
    }

    private static async Task<IResult> GetMembersAsync(long id, AppDbContext db, CancellationToken ct)
    {
        var members = await db.ChainMembers
            .Where(m => m.ChainId == id && m.Status == ChainMemberStatus.Active)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(ct);
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
        member.Status = ChainMemberStatus.Removed;
        member.RemovedAt = DateTimeOffset.UtcNow;
        member.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var chain = await db.Chains.FindAsync([chainId], ct);
        if (chain != null)
        {
            await tg.SyncChainMessageAsync(chain.Id, ct);
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

        var sanitizedUsername = request.Username != null ? string.Concat(request.Username.Where(c => !char.IsControl(c))).Trim() : string.Empty;
        var sanitizedNickname = request.TelegramNickname != null ? string.Concat(request.TelegramNickname.Where(c => !char.IsControl(c))).Trim() : string.Empty;

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(sanitizedUsername))
        {
            errors.Add("Username", ["用户名不能为空且不能只包含控制字符。"]);
        }
        else if (sanitizedUsername.Length > 50)
        {
            errors.Add("Username", ["用户名长度不能超过 50 个字符。"]);
        }

        if (string.IsNullOrWhiteSpace(sanitizedNickname))
        {
            errors.Add("TelegramNickname", ["Telegram 昵称不能为空且不能只包含控制字符。"]);
        }
        else if (sanitizedNickname.Length > 50)
        {
            errors.Add("TelegramNickname", ["Telegram 昵称长度不能超过 50 个字符。"]);
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors, detail: "提交的输入数据未通过验证。", title: "输入验证错误");
        }

        var beforeJson = System.Text.Json.JsonSerializer.Serialize(new { member.DisplayName, member.TelegramUsername });

        member.DisplayName = sanitizedUsername;
        member.TelegramUsername = sanitizedNickname;
        member.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var chain = await db.Chains.FindAsync([member.ChainId], ct);
        if (chain != null)
        {
            await tg.SyncChainMessageAsync(chain.Id, ct);
        }

        var afterJson = System.Text.Json.JsonSerializer.Serialize(new { member.DisplayName, member.TelegramUsername });
        await auditLog.LogAsync("UpdateMember", "ChainMember", id.ToString(), chatId: chain?.ChatId, beforeJson: beforeJson, afterJson: afterJson, success: true, actorType: "Admin");

        return Results.Ok();
    }

    private static async Task<IResult> GetAccountsAsync(
        AppDbContext db,
        string? search,
        string? status,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = db.AdminAccounts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchUpper = search.ToUpperInvariant();
            query = query.Where(a => a.NormalizedUsername.Contains(searchUpper));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(a => a.IsActive);
            }
            else if (status.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(a => !a.IsActive);
            }
        }

        var total = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling((double)total / pageSize);

        var items = await query
            .OrderBy(a => a.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.Username,
                Role = a.Role.ToString(),
                IsDisabled = !a.IsActive,
                a.CreatedAt,
                a.UpdatedAt
            })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            items,
            page,
            pageSize,
            total,
            totalPages
        });
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateAccountRequest request,
        AppDbContext db,
        IPasswordHasher<AdminAccount> hasher,
        AdminPasswordPolicy policy,
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

        if (!policy.Validate(request.Password, request.Username, null, out var policyError))
        {
            return Results.BadRequest(new { error = policyError });
        }

        var account = new AdminAccount
        {
            Username = request.Username,
            NormalizedUsername = request.Username.ToUpperInvariant(),
            Role = role,
            IsActive = true,
            PasswordHash = string.Empty,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        account.PasswordHash = hasher.HashPassword(account, request.Password);

        db.AdminAccounts.Add(account);
        await db.SaveChangesAsync(ct);

        await auditLog.LogAsync("CreateAdminAccount", "AdminAccount", account.Id.ToString(), success: true, actorType: "Admin");

        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> DisableAccountAsync(
        int id,
        HttpContext context,
        AppDbContext db,
        AdminSessionService sessionService,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var currentAdminIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(currentAdminIdClaim, out var currentAdminId) && currentAdminId == id)
        {
            return Results.BadRequest(new { error = "Cannot disable current admin account" });
        }

        var account = await db.AdminAccounts.FindAsync([id], ct);
        if (account == null) return Results.NotFound();

        if (account.Role == AdminRole.RootAdmin && account.IsActive)
        {
            var activeRootsCount = await db.AdminAccounts.CountAsync(a => a.Role == AdminRole.RootAdmin && a.IsActive, ct);
            if (activeRootsCount <= 1)
            {
                return Results.BadRequest(new { error = "Cannot disable the last active RootAdmin" });
            }
        }

        account.IsActive = false;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await sessionService.RevokeAllSessionsForAdminAsync(id, ct);
        await auditLog.LogAsync("DisableAdminAccount", "AdminAccount", id.ToString(), success: true, actorType: "Admin");

        return Results.Ok();
    }

    private static async Task<IResult> EnableAccountAsync(
        int id,
        AppDbContext db,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var account = await db.AdminAccounts.FindAsync([id], ct);
        if (account == null) return Results.NotFound();

        account.IsActive = true;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditLog.LogAsync("EnableAdminAccount", "AdminAccount", id.ToString(), success: true, actorType: "Admin");

        return Results.Ok();
    }

    public record UpdateAccountRequest(string Username, AdminRole Role);

    private static async Task<IResult> UpdateAccountAsync(
        int id,
        UpdateAccountRequest request,
        HttpContext context,
        AppDbContext db,
        AdminSessionService sessionService,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var account = await db.AdminAccounts.FindAsync([id], ct);
        if (account == null) return Results.NotFound();

        var normalizedUsername = request.Username.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            return Results.BadRequest(new { error = "Username cannot be empty" });
        }

        var usernameTaken = await db.AdminAccounts.AnyAsync(a => a.Id != id && a.NormalizedUsername == normalizedUsername, ct);
        if (usernameTaken)
        {
            return Results.BadRequest(new { error = "Username already taken" });
        }

        if (account.Role == AdminRole.RootAdmin && request.Role != AdminRole.RootAdmin && account.IsActive)
        {
            var activeRootsCount = await db.AdminAccounts.CountAsync(a => a.Role == AdminRole.RootAdmin && a.IsActive, ct);
            if (activeRootsCount <= 1)
            {
                return Results.BadRequest(new { error = "Cannot demote the last active RootAdmin" });
            }
        }

        var beforeJson = System.Text.Json.JsonSerializer.Serialize(new { account.Username, account.Role });

        account.Username = request.Username.Trim();
        account.NormalizedUsername = normalizedUsername;
        account.Role = request.Role;
        account.UpdatedAt = DateTimeOffset.UtcNow;

        account.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);

        await sessionService.RevokeAllSessionsForAdminAsync(id, ct);

        var afterJson = System.Text.Json.JsonSerializer.Serialize(new { account.Username, account.Role });
        await auditLog.LogAsync("UpdateAdminAccount", "AdminAccount", id.ToString(), beforeJson: beforeJson, afterJson: afterJson, success: true, actorType: "Admin");

        return Results.Ok();
    }

    private static async Task<IResult> RevokeSessionsAsync(
        int id,
        AdminSessionService sessionService,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        await sessionService.RevokeAllSessionsForAdminAsync(id, ct);
        await auditLog.LogAsync("RevokeAdminSessions", "AdminAccount", id.ToString(), success: true, actorType: "Admin");
        return Results.Ok();
    }

    private static async Task<IResult> ResetPasswordAsync(
        int id,
        ResetPasswordRequest request,
        AppDbContext db,
        IPasswordHasher<AdminAccount> hasher,
        AdminPasswordPolicy policy,
        AdminSessionService sessionService,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var account = await db.AdminAccounts.FindAsync([id], ct);
        if (account == null) return Results.NotFound();

        if (!policy.Validate(request.Password, account.Username, null, out var policyError))
        {
            return Results.BadRequest(new { error = policyError });
        }

        account.PasswordHash = hasher.HashPassword(account, request.Password);
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.PasswordChangedAt = DateTimeOffset.UtcNow;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await sessionService.RevokeAllSessionsForAdminAsync(id, ct);
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
        var totalPages = (int)Math.Ceiling((double)total / size);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .Select(l => new
            {
                l.Id,
                l.ActorType,
                l.ActorAdminId,
                ActorAdminUsername = db.AdminAccounts.Where(a => a.Id == l.ActorAdminId).Select(a => a.Username).FirstOrDefault(),
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
            })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            items = items,
            page = pageNum,
            pageSize = size,
            total,
            totalPages
        });
    }

    private static async ValueTask<object?> MustChangePasswordFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var adminIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(adminIdClaim, out var adminId))
            {
                var db = httpContext.RequestServices.GetRequiredService<AppDbContext>();
                var admin = await db.AdminAccounts.FindAsync([adminId], httpContext.RequestAborted);
                if (admin != null && admin.MustChangePassword)
                {
                    var path = httpContext.Request.Path.Value ?? "";
                    if (!path.EndsWith("/change-password", StringComparison.OrdinalIgnoreCase) &&
                        !path.EndsWith("/logout", StringComparison.OrdinalIgnoreCase) &&
                        !path.EndsWith("/login", StringComparison.OrdinalIgnoreCase) &&
                        !path.EndsWith("/csrf", StringComparison.OrdinalIgnoreCase) &&
                        !path.EndsWith("/auth/me", StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.Json(new { error = "You must change your password before performing this action." }, statusCode: StatusCodes.Status403Forbidden);
                    }
                }
            }
        }

        return await next(context);
    }

    public record LoginRequest(string Username, string Password);
    public record ChangePasswordRequest(string OldPassword, string NewPassword);
    public record UpdateMemberRequest(string Username, string TelegramNickname);
    public record CreateAccountRequest(string Username, string Password, string Role);
    public record ResetPasswordRequest(string Password);

    private static async Task<IResult> GetDashboardStatsAsync(AppDbContext db, CancellationToken ct)
    {
        var totalGroups = await db.ManagedChats.CountAsync(ct);
        var totalActiveChains = await db.Chains.CountAsync(c => c.Status == ChainStatus.Active && !c.DeletedAt.HasValue, ct);

        var todayStart = DateTimeOffset.UtcNow.Date;
        var totalJoinsToday = await db.ChainMembers.CountAsync(m => m.JoinedAt >= todayStart, ct);
        var totalAuditLogsToday = await db.AuditLogs.CountAsync(l => l.CreatedAt >= todayStart, ct);

        var creators = await db.Chains
            .Where(c => c.CreatedAt >= todayStart)
            .Select(c => c.CreatorTelegramUserId)
            .Distinct()
            .ToListAsync(ct);

        var joiners = await db.ChainMembers
            .Where(m => m.JoinedAt >= todayStart)
            .Select(m => m.TelegramUserId)
            .Distinct()
            .ToListAsync(ct);

        var leavers = await db.ChainMembers
            .Where(m => (m.LeftAt != null && m.LeftAt >= todayStart) || (m.RemovedAt != null && m.RemovedAt >= todayStart))
            .Select(m => m.TelegramUserId)
            .Distinct()
            .ToListAsync(ct);

        var activeUsersTodayCount = creators.Union(joiners).Union(leavers).Distinct().Count();

        return Results.Ok(new
        {
            total_groups = totalGroups,
            total_active_chains = totalActiveChains,
            total_joins_today = totalJoinsToday,
            total_audit_logs_today = totalAuditLogsToday,
            active_users_today_count = activeUsersTodayCount
        });
    }

    private static async Task<IResult> GetSystemSettingsAsync(AppDbContext db, CancellationToken ct)
    {
        var settings = await db.SystemSettings.FirstOrDefaultAsync(ct);
        if (settings == null)
        {
            settings = new SystemSetting
            {
                Id = 1,
                WhitelistMode = WhitelistMode.Enforced,
                UnauthorizedChatBehavior = "WarnAndLeave",
                DefaultCreatePolicy = CreatePolicy.Everyone,
                DefaultMaxMembers = 100,
                DefaultChainExpiryHours = 24,
                MaxActiveChainsPerChat = 5,
                TelegramInitDataMaxAgeSeconds = 86400,
                DeletedDataRetentionDays = 30,
                RequireMfaForSuperAdmin = false,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.SystemSettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(settings);
    }

    private static async Task<IResult> UpdateSystemSettingsAsync(
        SystemSetting request,
        HttpContext context,
        AppDbContext db,
        BotTokenProvider tokenProvider,
        TelegramService tg,
        AuditLogService auditLog,
        CancellationToken ct)
    {
        var settings = await db.SystemSettings.FirstOrDefaultAsync(ct);
        if (settings == null)
        {
            settings = new SystemSetting { Id = 1 };
            db.SystemSettings.Add(settings);
        }

        var beforeJson = System.Text.Json.JsonSerializer.Serialize(settings);

        var adminIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int? adminId = int.TryParse(adminIdClaim, out var parsedId) ? parsedId : null;

        var originalToken = tokenProvider.Token;
        if (!string.IsNullOrWhiteSpace(request.BotToken) && request.BotToken != originalToken)
        {
            try
            {
                // Temporarily update the token in provider
                tokenProvider.UpdateToken(request.BotToken);

                // Try calling getMe to verify the token is valid
                var botClient = tokenProvider.GetClient();
                await botClient.SendRequest(new Telegram.Bot.Requests.GetMeRequest(), ct);

                // Re-register webhook for the new bot
                await tg.EnsureWebhookAsync(ct);
            }
            catch (Exception ex)
            {
                // Roll back token in memory
                tokenProvider.UpdateToken(originalToken);
                return Results.BadRequest(new { error = $"Invalid Telegram Bot Token or connection error: {ex.Message}" });
            }
        }

        settings.WhitelistMode = request.WhitelistMode;
        settings.UnauthorizedChatBehavior = request.UnauthorizedChatBehavior;
        settings.DefaultCreatePolicy = request.DefaultCreatePolicy;
        settings.DefaultMaxMembers = request.DefaultMaxMembers;
        settings.DefaultChainExpiryHours = request.DefaultChainExpiryHours;
        settings.MaxActiveChainsPerChat = request.MaxActiveChainsPerChat;
        settings.TelegramInitDataMaxAgeSeconds = request.TelegramInitDataMaxAgeSeconds;
        settings.DeletedDataRetentionDays = request.DeletedDataRetentionDays;
        settings.RequireMfaForSuperAdmin = request.RequireMfaForSuperAdmin;
        settings.BotToken = request.BotToken;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedByAdminId = adminId;

        await db.SaveChangesAsync(ct);

        var afterJson = System.Text.Json.JsonSerializer.Serialize(settings);
        await auditLog.LogAsync("UpdateSystemSettings", "SystemSetting", "1", beforeJson: beforeJson, afterJson: afterJson, success: true, actorType: "Admin");

        return Results.Ok(settings);
    }
}
