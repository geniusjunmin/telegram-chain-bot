using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramChainBot.Bot;
using TelegramChainBot.Database;
using TelegramChainBot.Database.Models;
using TelegramChainBot.Security;
using TelegramChainBot.Services;
using Xunit;

namespace TelegramChainBot.UnitTests;

public class UpdateHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _contextOptions;
    private readonly AppDbContext _db;
    private readonly ITelegramBotClient _botClientMock;
    private readonly TelegramService _telegramServiceMock;
    private readonly ChainService _chainServiceMock;
    private readonly GroupAdminValidator _adminValidator;
    private readonly IConfiguration _configuration;

    public UpdateHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _contextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(_contextOptions);
        _db.Database.EnsureCreated();

        _botClientMock = Substitute.For<ITelegramBotClient>();
        
        var syncService = new TelegramMessageSyncService();
        var optionsMock = Microsoft.Extensions.Options.Options.Create(new TelegramChainBot.Options.BotOptions
        {
            BotToken = "12345:mock_token",
            WebhookBaseUrl = "https://mock.url"
        });
        _telegramServiceMock = Substitute.For<TelegramService>(_botClientMock, optionsMock, syncService, NullLogger<TelegramService>.Instance);
        _chainServiceMock = Substitute.For<ChainService>(_db);

        _adminValidator = new GroupAdminValidator(_botClientMock);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BOT_OWNER_IDS"] = "99999"
            })
            .Build();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Message CreateMockMessage(long chatId, ChatType chatType, string text, long userId, string username, string firstName)
    {
        var chatTypeStr = chatType switch
        {
            ChatType.Private => "private",
            ChatType.Group => "group",
            ChatType.Supergroup => "supergroup",
            _ => "private"
        };

        var json = $$"""
        {
            "MessageId": 100,
            "Date": 1783887913,
            "Chat": {
                "Id": {{chatId}},
                "Type": "{{chatTypeStr}}",
                "Title": "Mock Chat"
            },
            "From": {
                "Id": {{userId}},
                "Username": "{{username}}",
                "FirstName": "{{firstName}}"
            },
            "Text": "{{text}}"
        }
        """;

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return System.Text.Json.JsonSerializer.Deserialize<Message>(json, options)!;
    }

    [Fact]
    public async Task HandleAsync_LeavesChat_WhenGroupIsDisabledInWhitelist()
    {
        // Arrange
        var handler = new UpdateHandler(
            _chainServiceMock,
            _telegramServiceMock,
            _db,
            _adminValidator,
            _configuration,
            NullLogger<UpdateHandler>.Instance);

        var mockMsg = CreateMockMessage(-100123456L, ChatType.Supergroup, "/start_chain Pizza Dinner", 11111, "alice", "Alice");
        var update = new Update
        {
            Id = 1,
            Message = mockMsg
        };

        // Act
        await handler.HandleAsync(update, CancellationToken.None);

        // Assert
        await _telegramServiceMock.Received(1).LeaveChatAsync(-100123456, Arg.Any<CancellationToken>());
        
        var managed = await _db.ManagedChats.FindAsync(-100123456L);
        Assert.NotNull(managed);
        Assert.Equal(ManagedChatStatus.Disabled, managed.Status);
    }

    [Fact]
    public async Task HandleAsync_DoesNotRespond_WhenGroupIsInAuditMode()
    {
        // Arrange
        var handler = new UpdateHandler(
            _chainServiceMock,
            _telegramServiceMock,
            _db,
            _adminValidator,
            _configuration,
            NullLogger<UpdateHandler>.Instance);

        // Pre-register group as Audit
        _db.ManagedChats.Add(new ManagedChat
        {
            ChatId = -100123456,
            Title = "Audit Group",
            Status = ManagedChatStatus.Audit,
            CreatedAt = DateTimeOffset.UtcNow,
            AuthorizedBy = "Owner"
        });
        await _db.SaveChangesAsync();

        var mockMsg = CreateMockMessage(-100123456L, ChatType.Supergroup, "/start_chain Pizza Dinner", 11111, "alice", "Alice");
        var update = new Update
        {
            Id = 1,
            Message = mockMsg
        };

        // Act
        await handler.HandleAsync(update, CancellationToken.None);

        // Assert
        await _telegramServiceMock.DidNotReceive().LeaveChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _chainServiceMock.DidNotReceive().CreateChainAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AddsToWhitelist_WhenOwnerSendsWhitelistAddCommandInPrivateChat()
    {
        // Arrange
        var handler = new UpdateHandler(
            _chainServiceMock,
            _telegramServiceMock,
            _db,
            _adminValidator,
            _configuration,
            NullLogger<UpdateHandler>.Instance);

        var mockMsg = CreateMockMessage(99999, ChatType.Private, "/whitelist_add -100888888 Enforced", 99999, "owner_username", "Owner");
        var update = new Update
        {
            Id = 1,
            Message = mockMsg
        };

        // Act
        await handler.HandleAsync(update, CancellationToken.None);

        // Assert
        var managed = await _db.ManagedChats.FindAsync(-100888888L);
        Assert.NotNull(managed);
        Assert.Equal(ManagedChatStatus.Enforced, managed.Status);
        Assert.Equal("owner_username", managed.AuthorizedBy);

        await _telegramServiceMock.Received(1).SendTextMessageAsync(
            99999, 
            Arg.Is<string>(s => s.Contains("成功添加/更新白名单")), 
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_IgnoresWhitelistCommand_WhenNonOwnerSendsIt()
    {
        // Arrange
        var handler = new UpdateHandler(
            _chainServiceMock,
            _telegramServiceMock,
            _db,
            _adminValidator,
            _configuration,
            NullLogger<UpdateHandler>.Instance);

        var mockMsg = CreateMockMessage(11111, ChatType.Private, "/whitelist_add -100888888 Enforced", 11111, "alice", "Alice");
        var update = new Update
        {
            Id = 1,
            Message = mockMsg
        };

        // Act
        await handler.HandleAsync(update, CancellationToken.None);

        // Assert
        var managed = await _db.ManagedChats.FindAsync(-100888888L);
        Assert.Null(managed);

        await _telegramServiceMock.DidNotReceive().SendTextMessageAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
