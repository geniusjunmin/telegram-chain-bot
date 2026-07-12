using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramChainBot.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "chain_members",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChainId = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TelegramNickname = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, defaultValue: ""),
                    JoinTime = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chain_members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "chains",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatorId = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    ChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chains", x => x.Id);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_admins_Username",
                table: "admins",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chain_members_ChainId_UserId",
                table: "chain_members",
                columns: new[] { "ChainId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chains_PublicId",
                table: "chains",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admins");

            migrationBuilder.DropTable(
                name: "chain_members");

            migrationBuilder.DropTable(
                name: "chains");

            migrationBuilder.DropTable(
                name: "processed_telegram_updates");
        }
    }
}
