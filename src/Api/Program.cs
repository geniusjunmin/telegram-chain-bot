using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramChainBot.Api;
using TelegramChainBot.Bot;
using TelegramChainBot.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using TelegramChainBot.Database.Models;
using TelegramChainBot.Options;
using TelegramChainBot.Security;
using TelegramChainBot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<BotOptions>(opts =>
{
    opts.BotToken = builder.Configuration["BOT_TOKEN"] ?? string.Empty;
    opts.WebhookBaseUrl = builder.Configuration["WEBHOOK_BASE_URL"];
    opts.WebhookPath = builder.Configuration["WEBHOOK_PATH"] ?? "/telegram/webhook";
    opts.WebhookSecret = builder.Configuration["TELEGRAM_WEBHOOK_SECRET"];
});

builder.Services.AddDbContext<AppDbContext>(opts =>
{
    var dbPath = builder.Configuration["SQLITE_PATH"] ?? "data/chain.db";
    opts.UseSqlite($"Data Source={dbPath};Cache=Shared;Default Timeout=5;Foreign Keys=True");
});

builder.Services.AddSingleton<IPasswordHasher<AdminAccount>, PasswordHasher<AdminAccount>>();

var dataPath = builder.Configuration["DATA_PATH"] ?? "data";
var keysFolder = Path.Combine(dataPath, "dataprotection-keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("TelegramChainBot");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Name = "__Host-TelegramChain.Admin";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
            OnValidatePrincipal = async context =>
            {
                var sessionService = context.HttpContext.RequestServices.GetRequiredService<AdminSessionService>();
                var sessionId = context.Principal?.FindFirst("SessionId")?.Value;
                if (!sessionService.IsSessionActive(sessionId))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                var adminIdClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(adminIdClaim, out var adminId))
                {
                    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var admin = await db.AdminAccounts.FindAsync([adminId]);
                    
                    var securityStamp = context.Principal?.FindFirst("SecurityStamp")?.Value;
                    if (admin == null || !admin.IsActive || admin.SecurityStamp != securityStamp)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                }
                else
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin.Read", policy =>
        policy.RequireRole(AdminRole.RootAdmin.ToString(), AdminRole.OperatorAdmin.ToString(), AdminRole.AuditorAdmin.ToString()));
    options.AddPolicy("Admin.ManageChains", policy =>
        policy.RequireRole(AdminRole.RootAdmin.ToString(), AdminRole.OperatorAdmin.ToString()));
    options.AddPolicy("Admin.ManageSettings", policy =>
        policy.RequireRole(AdminRole.RootAdmin.ToString(), AdminRole.OperatorAdmin.ToString()));
    options.AddPolicy("Admin.ManageAccounts", policy =>
        policy.RequireRole(AdminRole.RootAdmin.ToString()));
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
});

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login-limiter", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 5;
        opt.QueueLimit = 0;
    });
});

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<BotOptions>>().Value;
    if (string.IsNullOrWhiteSpace(options.BotToken))
    {
        throw new InvalidOperationException("BOT_TOKEN is required.");
    }

    return new TelegramBotClient(options.BotToken);
});

builder.Services.AddScoped<ChainService>();
builder.Services.AddScoped<TelegramService>();
builder.Services.AddScoped<TelegramInitDataValidator>();
builder.Services.AddScoped<WebhookSecretValidator>();
builder.Services.AddScoped<BotSecurityService>();
builder.Services.AddScoped<UpdateHandler>();
builder.Services.AddScoped<BotService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddSingleton<AdminSessionService>();
builder.Services.AddSingleton<TelegramMessageSyncService>();
builder.Services.AddSingleton<GroupAdminValidator>();
builder.Services.AddScoped<DatabaseBootstrapper>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddHostedService<DatabaseBackupService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var bootstrapper = scope.ServiceProvider.GetRequiredService<DatabaseBootstrapper>();
    await bootstrapper.BootstrapAsync(CancellationToken.None);
    
    var adminService = scope.ServiceProvider.GetRequiredService<AdminService>();
    await adminService.EnsureDefaultAdminAsync(CancellationToken.None);

    var tg = scope.ServiceProvider.GetRequiredService<TelegramService>();
    await tg.EnsureWebhookAsync(CancellationToken.None);
}

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An unhandled exception occurred.");
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An error occurred while processing your request.",
            Detail = app.Environment.IsDevelopment() ? ex.ToString() : null,
            Instance = context.Request.Path
        };
        
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var json = JsonSerializer.Serialize(problem);
        await context.Response.WriteAsync(json);
    }
});

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'; script-src 'self' https://telegram.org; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self';");
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

var webAppPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "webapp"));
if (Directory.Exists(webAppPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(webAppPath),
        RequestPath = "/webapp"
    });
}
else
{
    var fallbackPath = Path.Combine(app.Environment.ContentRootPath, "webapp");
    if (Directory.Exists(fallbackPath))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(fallbackPath),
            RequestPath = "/webapp"
        });
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync(ct);
        if (canConnect)
        {
            return Results.Ok(new { status = "Healthy", database = "Connected" });
        }
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

// 彻底修复：使用固定路径 /telegram/webhook 并验证 X-Telegram-Bot-Api-Secret-Token 头
app.MapPost("/telegram/webhook", async (
    HttpContext context,
    WebhookSecretValidator secretValidator,
    BotService botService,
    CancellationToken cancellationToken) =>
{
    if (!context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? true)
    {
        return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
    }

    var secretHeader = context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
    if (!secretValidator.Validate(secretHeader))
    {
        return Results.Unauthorized();
    }

    try 
    {
        // Limit request size to 1MB (Telegram updates are usually small)
        if (context.Request.ContentLength > 1024 * 1024)
        {
            return Results.BadRequest("Request body too large.");
        }

        // 使用针对 Telegram.Bot v22 优化的 JsonBotAPI.Options
        var update = await JsonSerializer.DeserializeAsync<Update>(
            context.Request.Body, 
            JsonBotAPI.Options, 
            cancellationToken);

        if (update == null) return Results.BadRequest();

        await botService.HandleWebhookAsync(update, cancellationToken);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to handle Telegram update");
        return Results.BadRequest();
    }
    
    return Results.Ok();
});

app.MapChainEndpoints();
app.MapAdminEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/debug/error", () => { throw new InvalidOperationException("Test exception"); });
}

app.Run();

public partial class Program { }
