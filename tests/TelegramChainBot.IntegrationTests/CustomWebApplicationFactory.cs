using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using TelegramChainBot.Database;
using NSubstitute;

namespace TelegramChainBot.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BOT_TOKEN"] = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11",
                ["WEBHOOK_BASE_URL"] = "https://test.local",
                ["TELEGRAM_WEBHOOK_SECRET"] = "super_secret_webhook_token_32_bytes"
            });
        });

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD", "SuperSecurePassword123!");
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD_FILE", null);

        builder.ConfigureServices(services =>
        {
            // 1. Remove existing AppDbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // 2. Create and open a shared SqliteConnection for in-memory DB
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // 3. Mock ITelegramBotClient
            var botClientMock = Substitute.For<ITelegramBotClient>();

            var mockUser = new Telegram.Bot.Types.User
            {
                Id = 12345678,
                IsBot = true,
                FirstName = "TestBot",
                Username = "test_chain_bot"
            };

            botClientMock.SendRequest(
                Arg.Any<Telegram.Bot.Requests.Abstractions.IRequest<Telegram.Bot.Types.User>>(), 
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(mockUser));

            botClientMock.MakeRequest(
                Arg.Any<Telegram.Bot.Requests.Abstractions.IRequest<Telegram.Bot.Types.User>>(), 
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(mockUser));

            botClientMock.MakeRequestAsync(
                Arg.Any<Telegram.Bot.Requests.Abstractions.IRequest<Telegram.Bot.Types.User>>(), 
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(mockUser));

            var messageJson = "{\"message_id\":999,\"chat\":{\"id\":12345,\"type\":\"supergroup\"}}";
            var mockMessage = System.Text.Json.JsonSerializer.Deserialize<Telegram.Bot.Types.Message>(messageJson)!;

            botClientMock.SendRequest(
                Arg.Any<Telegram.Bot.Requests.Abstractions.IRequest<Telegram.Bot.Types.Message>>(), 
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(mockMessage));

            botClientMock.MakeRequest(
                Arg.Any<Telegram.Bot.Requests.Abstractions.IRequest<Telegram.Bot.Types.Message>>(), 
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(mockMessage));

            botClientMock.MakeRequestAsync(
                Arg.Any<Telegram.Bot.Requests.Abstractions.IRequest<Telegram.Bot.Types.Message>>(), 
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(mockMessage));
            
            // Remove existing ITelegramBotClient registration
            var botDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ITelegramBotClient));
            if (botDescriptor != null)
            {
                services.Remove(botDescriptor);
            }

            services.AddSingleton<ITelegramBotClient>(botClientMock);
        });

        builder.UseEnvironment("Development");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
        }
    }
}
