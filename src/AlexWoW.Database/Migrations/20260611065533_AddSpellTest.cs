using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexWoW.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSpellTest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "spell_test_result",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    session_id = table.Column<long>(type: "bigint", nullable: false),
                    spell_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    @class = table.Column<byte>(name: "class", type: "tinyint unsigned", nullable: false),
                    level = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    result_type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    school = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    amount = table.Column<uint>(type: "int unsigned", nullable: false),
                    effective = table.Column<uint>(type: "int unsigned", nullable: false),
                    overkill_or_overheal = table.Column<uint>(type: "int unsigned", nullable: false),
                    expected_min = table.Column<uint>(type: "int unsigned", nullable: false),
                    expected_max = table.Column<uint>(type: "int unsigned", nullable: false),
                    expected_cost = table.Column<uint>(type: "int unsigned", nullable: false),
                    power_type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    is_heal = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    weapon_based = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    family_name = table.Column<uint>(type: "int unsigned", nullable: false),
                    cast_index = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "datetime(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spell_test_result", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "spell_test_session",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    owner_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    account_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    @class = table.Column<byte>(name: "class", type: "tinyint unsigned", nullable: false),
                    level = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    mode = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    talents_slotted = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    started_at = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    ended_at = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    note = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    analyzed = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    ticket_id = table.Column<uint>(type: "int unsigned", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spell_test_session", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_str_session",
                table: "spell_test_result",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_str_spell",
                table: "spell_test_result",
                column: "spell_id");

            migrationBuilder.CreateIndex(
                name: "ix_sts_owner",
                table: "spell_test_session",
                column: "owner_guid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spell_test_result");

            migrationBuilder.DropTable(
                name: "spell_test_session");
        }
    }
}
