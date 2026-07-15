using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramChainBot.Migrations
{
    /// <inheritdoc />
    public partial class AddStaticVersionToSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StaticVersion",
                table: "system_settings",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "1.0.0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StaticVersion",
                table: "system_settings");
        }
    }
}
