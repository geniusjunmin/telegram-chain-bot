using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramChainBot.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_system_settings",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "system_settings");

            migrationBuilder.RenameColumn(
                name: "Value",
                table: "system_settings",
                newName: "UpdatedAt");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "system_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DefaultChainExpiryHours",
                table: "system_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DefaultCreatePolicy",
                table: "system_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DefaultMaxMembers",
                table: "system_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DeletedDataRetentionDays",
                table: "system_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxActiveChainsPerChat",
                table: "system_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequireMfaForSuperAdmin",
                table: "system_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TelegramInitDataMaxAgeSeconds",
                table: "system_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UnauthorizedChatBehavior",
                table: "system_settings",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "UpdatedByAdminId",
                table: "system_settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WhitelistMode",
                table: "system_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "MaxActiveChains",
                table: "managed_chats",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 5);

            migrationBuilder.AlterColumn<bool>(
                name: "IsJoinEnabled",
                table: "managed_chats",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<int>(
                name: "DefaultMaxMembers",
                table: "managed_chats",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 100);

            migrationBuilder.AddPrimaryKey(
                name: "PK_system_settings",
                table: "system_settings",
                column: "Id");

            migrationBuilder.Sql(
                "INSERT OR IGNORE INTO system_settings (Id, WhitelistMode, UnauthorizedChatBehavior, DefaultCreatePolicy, DefaultMaxMembers, DefaultChainExpiryHours, MaxActiveChainsPerChat, TelegramInitDataMaxAgeSeconds, DeletedDataRetentionDays, RequireMfaForSuperAdmin, UpdatedAt) " +
                "VALUES (1, 3, 'WarnAndLeave', 1, 100, 24, 5, 86400, 30, 0, datetime('now'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_system_settings",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "DefaultChainExpiryHours",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "DefaultCreatePolicy",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "DefaultMaxMembers",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "DeletedDataRetentionDays",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "MaxActiveChainsPerChat",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "RequireMfaForSuperAdmin",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "TelegramInitDataMaxAgeSeconds",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "UnauthorizedChatBehavior",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "UpdatedByAdminId",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "WhitelistMode",
                table: "system_settings");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "system_settings",
                newName: "Value");

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "system_settings",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<int>(
                name: "MaxActiveChains",
                table: "managed_chats",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<bool>(
                name: "IsJoinEnabled",
                table: "managed_chats",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "DefaultMaxMembers",
                table: "managed_chats",
                type: "INTEGER",
                nullable: false,
                defaultValue: 100,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddPrimaryKey(
                name: "PK_system_settings",
                table: "system_settings",
                column: "Key");
        }
    }
}
