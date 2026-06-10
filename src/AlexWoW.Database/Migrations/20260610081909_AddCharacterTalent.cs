using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexWoW.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterTalent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_talent",
                columns: table => new
                {
                    owner_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    talent_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    rank = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_talent", x => new { x.owner_guid, x.talent_id });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_talent_owner",
                table: "character_talent",
                column: "owner_guid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_talent");
        }
    }
}
