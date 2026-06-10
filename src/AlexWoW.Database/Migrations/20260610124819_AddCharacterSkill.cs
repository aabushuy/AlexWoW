using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexWoW.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterSkill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_skill",
                columns: table => new
                {
                    owner_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    skill_id = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    value = table.Column<ushort>(type: "smallint unsigned", nullable: false, defaultValue: (ushort)0),
                    max_value = table.Column<ushort>(type: "smallint unsigned", nullable: false, defaultValue: (ushort)0),
                    step = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_skill", x => new { x.owner_guid, x.skill_id });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_skill_owner",
                table: "character_skill",
                column: "owner_guid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_skill");
        }
    }
}
