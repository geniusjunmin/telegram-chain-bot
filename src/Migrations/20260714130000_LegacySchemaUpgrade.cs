using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;
using TelegramChainBot.Database;

#nullable disable

namespace TelegramChainBot.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260714130000_LegacySchemaUpgrade")]
    public partial class LegacySchemaUpgrade : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Upgrade admins to admin_accounts
            migrationBuilder.RenameTable(name: "admins", newName: "admin_accounts");
            
            migrationBuilder.AddColumn<string>(name: "NormalizedUsername", table: "admin_accounts", maxLength: 64, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<int>(name: "Role", table: "admin_accounts", nullable: false, defaultValue: 3);
            migrationBuilder.AddColumn<bool>(name: "IsActive", table: "admin_accounts", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "MustChangePassword", table: "admin_accounts", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<int>(name: "AccessFailedCount", table: "admin_accounts", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "LockoutEnd", table: "admin_accounts", nullable: true);
            migrationBuilder.AddColumn<string>(name: "SecurityStamp", table: "admin_accounts", maxLength: 64, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "LastLoginAt", table: "admin_accounts", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PasswordChangedAt", table: "admin_accounts", nullable: true);
            migrationBuilder.AddColumn<string>(name: "CreatedAt", table: "admin_accounts", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "UpdatedAt", table: "admin_accounts", nullable: false, defaultValue: "");

            // Populate CreatedAt/UpdatedAt/SecurityStamp/NormalizedUsername for old admins
            migrationBuilder.Sql("UPDATE admin_accounts SET NormalizedUsername = upper(Username), CreatedAt = datetime('now'), UpdatedAt = datetime('now'), SecurityStamp = lower(hex(randomblob(16)));");
            
            migrationBuilder.CreateIndex(
                name: "IX_admin_accounts_NormalizedUsername",
                table: "admin_accounts",
                column: "NormalizedUsername",
                unique: true);

            // 2. Rebuild chains to upgrade schema and generate random PublicId
            migrationBuilder.Sql("PRAGMA foreign_keys=OFF;");
            migrationBuilder.Sql(
                "CREATE TABLE chains_new (" +
                "Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
                "PublicId TEXT NOT NULL, " +
                "ChatId INTEGER NOT NULL, " +
                "CreatorTelegramUserId INTEGER NOT NULL, " +
                "Title TEXT NOT NULL, " +
                "Status INTEGER NOT NULL, " +
                "MessageId INTEGER NULL, " +
                "MaxMembers INTEGER NOT NULL, " +
                "ExpiresAt TEXT NULL, " +
                "CreatedAt TEXT NOT NULL, " +
                "UpdatedAt TEXT NOT NULL, " +
                "ClosedAt TEXT NULL, " +
                "ClosedByTelegramUserId INTEGER NULL, " +
                "ClosedByAdminId INTEGER NULL, " +
                "DeletedAt TEXT NULL, " +
                "DeletedByAdminId INTEGER NULL, " +
                "TelegramSyncStatus INTEGER NOT NULL, " +
                "LastSyncError TEXT NULL, " +
                "LastSyncedAt TEXT NULL, " +
                "Version INTEGER NOT NULL" +
                ");");

            // Copy old data while generating lowercase hex GUIDs for PublicId
            migrationBuilder.Sql(
                "INSERT INTO chains_new (Id, PublicId, ChatId, CreatorTelegramUserId, Title, Status, MessageId, MaxMembers, CreatedAt, UpdatedAt, TelegramSyncStatus, Version) " +
                "SELECT Id, lower(hex(randomblob(16))), ChatId, CreatorId, Title, 2, MessageId, 100, CreatedAt, CreatedAt, 2, 1 FROM chains;");

            migrationBuilder.Sql("DROP TABLE chains;");
            migrationBuilder.Sql("ALTER TABLE chains_new RENAME TO chains;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IX_chains_PublicId ON chains (PublicId);");

            // 3. Rebuild chain_members to match the new schema
            migrationBuilder.Sql(
                "CREATE TABLE chain_members_new (" +
                "Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
                "ChainId INTEGER NOT NULL, " +
                "TelegramUserId INTEGER NOT NULL, " +
                "DisplayName TEXT NOT NULL, " +
                "TelegramUsername TEXT NULL, " +
                "TelegramFullName TEXT NULL, " +
                "Status INTEGER NOT NULL, " +
                "JoinedAt TEXT NOT NULL, " +
                "UpdatedAt TEXT NOT NULL, " +
                "LeftAt TEXT NULL, " +
                "RemovedAt TEXT NULL" +
                ");");

            migrationBuilder.Sql(
                "INSERT INTO chain_members_new (Id, ChainId, TelegramUserId, DisplayName, TelegramUsername, Status, JoinedAt, UpdatedAt) " +
                "SELECT Id, ChainId, UserId, Username, TelegramNickname, 1, JoinTime, JoinTime FROM chain_members;");

            migrationBuilder.Sql("DROP TABLE chain_members;");
            migrationBuilder.Sql("ALTER TABLE chain_members_new RENAME TO chain_members;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IX_chain_members_ChainId_TelegramUserId ON chain_members (ChainId, TelegramUserId);");
            migrationBuilder.Sql("PRAGMA foreign_keys=ON;");

            // 4. Create remaining tables
            migrationBuilder.CreateTable(
                name: "processed_telegram_updates",
                columns: table => new
                {
                    UpdateId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceivedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_telegram_updates", x => x.UpdateId);
                });

            migrationBuilder.CreateTable(
                name: "managed_chats",
                columns: table => new
                {
                    ChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ChatType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AuthorizationStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatePolicy = table.Column<int>(type: "INTEGER", nullable: false),
                    IsJoinEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    DefaultMaxMembers = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 100),
                    MaxActiveChains = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 5),
                    LastSeenAt = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovedAt = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedByAdminId = table.Column<int>(type: "INTEGER", nullable: true),
                    BlockedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_chats", x => x.ChatId);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActorType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActorAdminId = table.Column<int>(type: "INTEGER", nullable: true),
                    ActorTelegramUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ChatId = table.Column<long>(type: "INTEGER", nullable: true),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: false),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddressHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_settings", x => x.Key);
                });

            // 5. Populate ManagedChats with history chat IDs from chains
            migrationBuilder.Sql(
                "INSERT OR IGNORE INTO managed_chats (ChatId, Title, ChatType, AuthorizationStatus, CreatePolicy, LastSeenAt, CreatedAt, UpdatedAt) " +
                "SELECT DISTINCT ChatId, 'Migrated Group', 'supergroup', 2, 1, datetime('now'), datetime('now'), datetime('now') FROM chains;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop new tables
            migrationBuilder.DropTable(name: "system_settings");
            migrationBuilder.DropTable(name: "audit_logs");
            migrationBuilder.DropTable(name: "managed_chats");
            migrationBuilder.DropTable(name: "processed_telegram_updates");

            // Rebuild chain_members to legacy format
            migrationBuilder.Sql("PRAGMA foreign_keys=OFF;");
            migrationBuilder.Sql(
                "CREATE TABLE chain_members_old (" +
                "Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
                "ChainId INTEGER NOT NULL, " +
                "UserId INTEGER NOT NULL, " +
                "Username TEXT NOT NULL, " +
                "TelegramNickname TEXT NOT NULL, " +
                "JoinTime TEXT NOT NULL" +
                ");");
            migrationBuilder.Sql(
                "INSERT INTO chain_members_old (Id, ChainId, UserId, Username, TelegramNickname, JoinTime) " +
                "SELECT Id, ChainId, TelegramUserId, DisplayName, ifnull(TelegramUsername, ''), JoinedAt FROM chain_members;");
            migrationBuilder.Sql("DROP TABLE chain_members;");
            migrationBuilder.Sql("ALTER TABLE chain_members_old RENAME TO chain_members;");

            // Rebuild chains to legacy format
            migrationBuilder.Sql(
                "CREATE TABLE chains_old (" +
                "Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
                "Title TEXT NOT NULL, " +
                "CreatorId INTEGER NOT NULL, " +
                "MessageId INTEGER NOT NULL, " +
                "ChatId INTEGER NOT NULL, " +
                "CreatedAt TEXT NOT NULL" +
                ");");
            migrationBuilder.Sql(
                "INSERT INTO chains_old (Id, Title, CreatorId, MessageId, ChatId, CreatedAt) " +
                "SELECT Id, Title, CreatorTelegramUserId, ifnull(MessageId, 0), ChatId, CreatedAt FROM chains;");
            migrationBuilder.Sql("DROP TABLE chains;");
            migrationBuilder.Sql("ALTER TABLE chains_old RENAME TO chains;");

            // Rename admin_accounts back to admins and drop columns
            migrationBuilder.RenameTable(name: "admin_accounts", newName: "admins");
            
            // Drop columns from admins by rebuilding it
            migrationBuilder.Sql(
                "CREATE TABLE admins_old (" +
                "Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
                "Username TEXT NOT NULL, " +
                "PasswordHash TEXT NOT NULL" +
                ");");
            migrationBuilder.Sql("INSERT INTO admins_old (Id, Username, PasswordHash) SELECT Id, Username, PasswordHash FROM admins;");
            migrationBuilder.Sql("DROP TABLE admins;");
            migrationBuilder.Sql("ALTER TABLE admins_old RENAME TO admins;");
            migrationBuilder.Sql("PRAGMA foreign_keys=ON;");
        }
    }
}
