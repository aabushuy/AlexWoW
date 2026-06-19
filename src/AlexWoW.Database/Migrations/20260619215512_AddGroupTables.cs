using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexWoW.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "group_data",
                columns: table => new
                {
                    id = table.Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    leader_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    leader_name = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    loot_method = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    loot_master_guid = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_data", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "group_member",
                columns: table => new
                {
                    group_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    char_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    subgroup = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    is_assistant = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_member", x => new { x.group_id, x.char_guid });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_gm_char",
                table: "group_member",
                column: "char_guid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_data");

            migrationBuilder.DropTable(
                name: "group_member");
        }
    }
}
