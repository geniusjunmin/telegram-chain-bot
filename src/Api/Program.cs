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

    var tg = scope.ServiceProvider.GetRequiredService<TelegramService>();
    await tg.EnsureWebhookAsync(CancellationToken.None);
}

var webAppPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "webapp"));
app.Logger.LogInformation("Checking for webapp folder at: {Path}", webAppPath);
if (Directory.Exists(webAppPath))
{
    app.Logger.LogInformation("Found webapp folder.");
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(webAppPath),
        RequestPath = "/webapp"
    });
}
else
{
    app.Logger.LogWarning("Webapp folder NOT found at {Path}", webAppPath);
    var fallbackPath = Path.Combine(app.Environment.ContentRootPath, "webapp");
    if (Directory.Exists(fallbackPath))
    {
        app.Logger.LogInformation("Found webapp folder at fallback path: {Path}", fallbackPath);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(fallbackPath),
            RequestPath = "/webapp"
        });
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

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

    // Use System.Text.Json with JsonBotAPI.Options which is the standard way for v22
    using var reader = new StreamReader(context.Request.Body);
    var json = await reader.ReadToEndAsync(cancellationToken);
    
    try 
    {
        var update = JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options);
        if (update == null) return Results.BadRequest();

        await botService.HandleWebhookAsync(update, cancellationToken);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error deserializing Telegram update");
        return Results.BadRequest();
    }
    
    return Results.Ok();
});

app.MapChainEndpoints();

app.Run();
