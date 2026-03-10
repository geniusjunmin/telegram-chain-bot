using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramChainBot.Api;
using TelegramChainBot.Bot;
using TelegramChainBot.Database;
using TelegramChainBot.Options;
using TelegramChainBot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<BotOptions>(opts =>
{
    opts.BotToken = builder.Configuration["BOT_TOKEN"] ?? string.Empty;
    opts.WebhookBaseUrl = builder.Configuration["WEBHOOK_BASE_URL"];
    opts.WebhookPath = builder.Configuration["WEBHOOK_PATH"] ?? "/telegram/webhook";
});

builder.Services.AddDbContext<AppDbContext>(opts =>
{
    var dbPath = builder.Configuration["SQLITE_PATH"] ?? "data/chain.db";
    if (!Path.IsPathRooted(dbPath))
    {
        dbPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", dbPath));
    }
    opts.UseSqlite($"Data Source={dbPath}");
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
builder.Services.AddScoped<BotSecurityService>();
builder.Services.AddScoped<UpdateHandler>();
builder.Services.AddScoped<BotService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    // 仅在 Token 存在时尝试设置 Webhook，避免启动时崩溃
    var options = scope.ServiceProvider.GetRequiredService<IOptions<BotOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.BotToken))
    {
        var tg = scope.ServiceProvider.GetRequiredService<TelegramService>();
        await tg.EnsureWebhookAsync(CancellationToken.None);
    }
}

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

// 彻底修复：不再直接使用 Update update 参数，而是手动用官方 Options 解析
app.MapPost("/telegram/webhook/{token}", async (
    string token,
    HttpContext context,
    IOptions<BotOptions> options,
    BotService botService,
    CancellationToken cancellationToken) =>
{
    if (!string.Equals(token, options.Value.BotToken, StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }

    try 
    {
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

app.Run();
