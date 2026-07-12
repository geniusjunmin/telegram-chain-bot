using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramChainBot.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "admins",
                newName: "admin_accounts");

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "admin_accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3); // 3 = AuditorAdmin

            migrationBuilder.AddColumn<string>(
                name: "LockoutEnd",
                table: "admin_accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AccessFailedCount",
                table: "admin_accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDisabled",
                table: "admin_accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "admin_accounts");

            migrationBuilder.DropColumn(
                name: "LockoutEnd",
                table: "admin_accounts");

            migrationBuilder.DropColumn(
                name: "AccessFailedCount",
                table: "admin_accounts");

            migrationBuilder.DropColumn(
                name: "IsDisabled",
                table: "admin_accounts");

            migrationBuilder.RenameTable(
                name: "admin_accounts",
                newName: "admins");
        }
    }
}
