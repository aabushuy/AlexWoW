using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexWoW.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guild_data",
                columns: table => new
                {
                    id = table.Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    leader_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    motd = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    info_text = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    emblem_style = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    emblem_color = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    border_style = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    border_color = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    background_color = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guild_data", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "guild_member",
                columns: table => new
                {
                    guild_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    char_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    rank_id = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    public_note = table.Column<string>(type: "varchar(31)", maxLength: 31, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    officer_note = table.Column<string>(type: "varchar(31)", maxLength: 31, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    joined_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guild_member", x => new { x.guild_id, x.char_guid });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "guild_rank",
                columns: table => new
                {
                    guild_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    rank_id = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    name = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rights = table.Column<uint>(type: "int unsigned", nullable: false),
                    bank_money_per_day = table.Column<int>(type: "int", nullable: false, defaultValue: -1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guild_rank", x => new { x.guild_id, x.rank_id });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ux_guild_name",
                table: "guild_data",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guild_member_char",
                table: "guild_member",
                column: "char_guid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_data");

            migrationBuilder.DropTable(
                name: "guild_member");

            migrationBuilder.DropTable(
                name: "guild_rank");
        }
    }
}
