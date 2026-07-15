using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;
using TelegramChainBot.Options;
using TelegramChainBot.Services;
using Xunit;

namespace TelegramChainBot.UnitTests;

public class TelegramServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _contextOptions;
    private readonly AppDbContext _db;
    private readonly ITelegramBotClient _botClientMock;
    private readonly IServiceProvider _serviceProvider;

    public TelegramServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _contextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(_contextOptions);
        _db.Database.EnsureCreated();

        _botClientMock = Substitute.For<ITelegramBotClient>();

        var mockUser = new User
        {
            Id = 12345,
            IsBot = true,
            FirstName = "Test Bot",
            Username = "test_chain_bot"
        };
        _botClientMock.SendRequest(
            Arg.Any<Telegram.Bot.Requests.Abstractions.IRequest<User>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockUser));

        var services = new ServiceCollection();
        services.AddSingleton(_db);
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SyncChainMessageAsync_UpdatesMessageWithCorrectSuffixAndStatus()
    {
        // Arrange
        var chain = new Chain
        {
            Id = 1,
            PublicId = "sync_test",
            ChatId = -100123456L,
            CreatorTelegramUserId = 11111,
            Title = "Friday Dinner",
            MessageId = 7777,
            Status = ChainStatus.Closed
        };
        _db.Chains.Add(chain);

        var member = new ChainMember
        {
            ChainId = 1,
            TelegramUserId = 22222,
            DisplayName = "Bob",
            JoinedAt = DateTimeOffset.UtcNow,
            Status = ChainMemberStatus.Active
        };
        _db.ChainMembers.Add(member);
        await _db.SaveChangesAsync();

        var syncService = new TelegramMessageSyncService();
        var optionsMock = Microsoft.Extensions.Options.Options.Create(new BotOptions
        {
            BotToken = "12345:mock_token",
            WebhookBaseUrl = "https://mock.url"
        });

        var telegramService = new TelegramService(
            _botClientMock,
            optionsMock,
            syncService,
            _serviceProvider,
            NullLogger<TelegramService>.Instance);

        // Stub SendRequest to return a dummy Message
        _botClientMock.SendRequest(
            Arg.Any<Telegram.Bot.Requests.Abstractions.IRequest<Message>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Message()));

        // Act
        await telegramService.SyncChainMessageAsync(1, CancellationToken.None);

        // Assert
        // Verify SendRequest was called with correct suffix and formatting
        await _botClientMock.Received(1).SendRequest(
            Arg.Is<Telegram.Bot.Requests.Abstractions.IRequest<Message>>(req =>
                req is Telegram.Bot.Requests.EditMessageTextRequest &&
                ((Telegram.Bot.Requests.EditMessageTextRequest)req).ChatId.Identifier == -100123456L &&
                ((Telegram.Bot.Requests.EditMessageTextRequest)req).MessageId == 7777 &&
                ((Telegram.Bot.Requests.EditMessageTextRequest)req).Text.Contains("Friday Dinner") &&
                ((Telegram.Bot.Requests.EditMessageTextRequest)req).Text.Contains("Bob") &&
                ((Telegram.Bot.Requests.EditMessageTextRequest)req).Text.Contains("⚠️ 接龙已关闭。") &&
                ((Telegram.Bot.Requests.EditMessageTextRequest)req).Text.Contains("[同步于:")),
            Arg.Any<CancellationToken>());

        // Verify sync status in DB is Synced
        var updatedChain = await _db.Chains.FindAsync(1L);
        Assert.NotNull(updatedChain);
        Assert.Equal(TelegramSyncStatus.Synced, updatedChain.TelegramSyncStatus);
        Assert.Null(updatedChain.LastSyncError);
        Assert.NotNull(updatedChain.LastSyncedAt);
    }

    [Fact]
    public async Task SyncChainMessageAsync_Handles429RateLimit_AndSavesErrorOnFailure()
    {
        // Arrange
        var chain = new Chain
        {
            Id = 2,
            PublicId = "rate_limit_test",
            ChatId = -100123456L,
            CreatorTelegramUserId = 11111,
            Title = "Saturday Lunch",
            MessageId = 8888,
            Status = ChainStatus.Active
        };
        _db.Chains.Add(chain);
        await _db.SaveChangesAsync();

        var syncService = new TelegramMessageSyncService();
        var optionsMock = Microsoft.Extensions.Options.Options.Create(new BotOptions
        {
            BotToken = "12345:mock_token",
            WebhookBaseUrl = "https://mock.url"
        });

        var telegramService = new TelegramService(
            _botClientMock,
            optionsMock,
            syncService,
            _serviceProvider,
            NullLogger<TelegramService>.Instance);

        // Stub SendRequest to throw a 429 ApiRequestException
        var parameters = new ResponseParameters { RetryAfter = 1 };
        var exception = new Telegram.Bot.Exceptions.ApiRequestException("Too Many Requests", 429, parameters);

        _botClientMock.SendRequest(
            Arg.Any<Telegram.Bot.Requests.Abstractions.IRequest<Message>>(),
            Arg.Any<CancellationToken>())
            .Throws(exception);

        // Act & Assert
        await Assert.ThrowsAsync<Telegram.Bot.Exceptions.ApiRequestException>(async () =>
        {
            await telegramService.SyncChainMessageAsync(2, CancellationToken.None);
        });

        // Verify that the status in the DB is set to Failed
        var updatedChain = await _db.Chains.FindAsync(2L);
        Assert.NotNull(updatedChain);
        Assert.Equal(TelegramSyncStatus.Failed, updatedChain.TelegramSyncStatus);
        Assert.Equal("Too Many Requests", updatedChain.LastSyncError);
        Assert.NotNull(updatedChain.LastSyncedAt);
    }
}
