using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexWoW.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPetTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_pet",
                columns: table => new
                {
                    id = table.Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    owner_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    entry = table.Column<uint>(type: "int unsigned", nullable: false),
                    name = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    level = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    experience = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    health = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    max_health = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    mana = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    max_mana = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    type = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    react_state = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)1),
                    command_state = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)1),
                    happiness = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    summoned_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_pet", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ux_charpet_owner",
                table: "character_pet",
                column: "owner_guid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_pet");
        }
    }
}
