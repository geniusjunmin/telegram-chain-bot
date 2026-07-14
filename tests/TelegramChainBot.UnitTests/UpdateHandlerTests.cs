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
                ["BOT_OWNER_IDS"] = "99999",
                ["GLOBAL_WHITELIST_MODE"] = "Enforced"
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

        var entitiesJson = "";
        if (text.StartsWith('/'))
        {
            var firstSpace = text.IndexOf(' ');
            var commandLen = firstSpace >= 0 ? firstSpace : text.Length;
            entitiesJson = ",\n\"Entities\": [\n{\n\"Type\": \"bot_command\",\n\"Offset\": 0,\n\"Length\": " + commandLen + "\n}\n]";
        }

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
            "Text": "{{text}}"{{entitiesJson}}
        }
        """;

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return System.Text.Json.JsonSerializer.Deserialize<Message>(json, options)!;
    }

    [Fact]
    public async Task HandleAsync_LeavesChat_WhenGroupIsBlockedInWhitelist()
    {
        // Arrange
        var handler = new UpdateHandler(
            _chainServiceMock,
            _telegramServiceMock,
            _db,
            _adminValidator,
            _configuration,
            NullLogger<UpdateHandler>.Instance);

        // Pre-register group as Blocked
        _db.ManagedChats.Add(new ManagedChat
        {
            ChatId = -100123456,
            Title = "Blocked Group",
            ChatType = "supergroup",
            AuthorizationStatus = AuthorizationStatus.Blocked,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
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
        await _telegramServiceMock.Received(1).LeaveChatAsync(-100123456, Arg.Any<CancellationToken>());
        
        var managed = await _db.ManagedChats.FindAsync(-100123456L);
        Assert.NotNull(managed);
        Assert.Equal(AuthorizationStatus.Blocked, managed.AuthorizationStatus);
    }

    [Fact]
    public async Task HandleAsync_DoesNotRespond_WhenGroupIsPendingInWhitelist()
    {
        // Arrange
        var handler = new UpdateHandler(
            _chainServiceMock,
            _telegramServiceMock,
            _db,
            _adminValidator,
            _configuration,
            NullLogger<UpdateHandler>.Instance);

        // Pre-register group as Pending
        _db.ManagedChats.Add(new ManagedChat
        {
            ChatId = -100123456,
            Title = "Pending Group",
            ChatType = "supergroup",
            AuthorizationStatus = AuthorizationStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
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

        var mockMsg = CreateMockMessage(99999, ChatType.Private, "/whitelist_add -100888888 Approved", 99999, "owner_username", "Owner");
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
        Assert.Equal(AuthorizationStatus.Approved, managed.AuthorizationStatus);

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

        var mockMsg = CreateMockMessage(11111, ChatType.Private, "/whitelist_add -100888888 Approved", 11111, "alice", "Alice");
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

    [Fact]
    public async Task HandleAsync_UpdatesCreatePolicy_WhenAdminSendsChainAdminOnly()
    {
        // Arrange
        var handler = new UpdateHandler(
            _chainServiceMock,
            _telegramServiceMock,
            _db,
            _adminValidator,
            _configuration,
            NullLogger<UpdateHandler>.Instance);

        var chatId = -100777777L;
        _db.ManagedChats.Add(new ManagedChat
        {
            ChatId = chatId,
            Title = "Config Group",
            ChatType = "supergroup",
            AuthorizationStatus = AuthorizationStatus.Approved,
            CreatePolicy = CreatePolicy.Everyone,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockMember = new ChatMemberOwner();
        _botClientMock.GetChatMember(chatId, 99999L, Arg.Any<CancellationToken>()).Returns(mockMember);

        var mockMsg = CreateMockMessage(chatId, ChatType.Supergroup, "/chain_admin_only on", 99999L, "owner_username", "Owner");
        var update = new Update
        {
            Id = 1,
            Message = mockMsg
        };

        // Act
        await handler.HandleAsync(update, CancellationToken.None);

        // Assert
        var managed = await _db.ManagedChats.FindAsync(chatId);
        Assert.NotNull(managed);
        Assert.Equal(CreatePolicy.ChatAdministrators, managed.CreatePolicy);

        await _telegramServiceMock.Received(1).SendTextMessageAsync(
            chatId,
            Arg.Is<string>(s => s.Contains("接龙创建策略已更新：仅限群管理员创建接龙")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BlocksChainCreation_WhenNonAdminSendsStartChainUnderAdminOnly()
    {
        // Arrange
        var handler = new UpdateHandler(
            _chainServiceMock,
            _telegramServiceMock,
            _db,
            _adminValidator,
            _configuration,
            NullLogger<UpdateHandler>.Instance);

        var chatId = -100777778L;
        _db.ManagedChats.Add(new ManagedChat
        {
            ChatId = chatId,
            Title = "Restricted Group",
            ChatType = "supergroup",
            AuthorizationStatus = AuthorizationStatus.Approved,
            CreatePolicy = CreatePolicy.ChatAdministrators,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var mockMember = new ChatMemberMember();
        _botClientMock.GetChatMember(chatId, 88888L, Arg.Any<CancellationToken>()).Returns(mockMember);

        var mockMsg = CreateMockMessage(chatId, ChatType.Supergroup, "/start_chain Sunday Run", 88888L, "alice", "Alice");
        var update = new Update
        {
            Id = 1,
            Message = mockMsg
        };

        // Act
        await handler.HandleAsync(update, CancellationToken.None);

        // Assert
        await _chainServiceMock.DidNotReceive().CreateChainAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _telegramServiceMock.Received(1).SendTextMessageAsync(
            chatId,
            Arg.Is<string>(s => s.Contains("只有群管理员才能发起接龙")),
            Arg.Any<CancellationToken>());
    }
}
