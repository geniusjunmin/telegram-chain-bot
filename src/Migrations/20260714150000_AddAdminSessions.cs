using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;
using TelegramChainBot.Database;

#nullable disable

namespace TelegramChainBot.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260714150000_AddAdminSessions")]
    public partial class AddAdminSessions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_sessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AdminId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<string>(type: "TEXT", nullable: false),
                    IsRevoked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_sessions", x => x.SessionId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_sessions");
        }
    }
}
