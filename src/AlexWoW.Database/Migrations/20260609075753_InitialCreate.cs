using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexWoW.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "account",
                columns: table => new
                {
                    id = table.Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    username = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    salt = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    verifier = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    session_key = table.Column<byte[]>(type: "binary(40)", nullable: true),
                    last_ip = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    is_admin = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "account_data",
                columns: table => new
                {
                    owner_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    is_char = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    data_type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    update_time = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    data = table.Column<byte[]>(type: "longblob", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_data", x => new { x.owner_id, x.is_char, x.data_type });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "character_action",
                columns: table => new
                {
                    owner_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    button = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    packed_data = table.Column<uint>(type: "int unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_action", x => new { x.owner_guid, x.button });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "character_aura",
                columns: table => new
                {
                    owner_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    spell = table.Column<uint>(type: "int unsigned", nullable: false),
                    form = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_aura", x => new { x.owner_guid, x.spell });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "character_declined_names",
                columns: table => new
                {
                    owner_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    n0 = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    n1 = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    n2 = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    n3 = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    n4 = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_declined_names", x => x.owner_guid);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "character_items",
                columns: table => new
                {
                    item_guid = table.Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    owner_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    item_entry = table.Column<uint>(type: "int unsigned", nullable: false),
                    bag = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)255),
                    slot = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    stack_count = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 1u)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_items", x => x.item_guid);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "character_queststatus",
                columns: table => new
                {
                    owner_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    quest_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    slot = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    status = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    counter0 = table.Column<ushort>(type: "smallint unsigned", nullable: false, defaultValue: (ushort)0),
                    counter1 = table.Column<ushort>(type: "smallint unsigned", nullable: false, defaultValue: (ushort)0),
                    counter2 = table.Column<ushort>(type: "smallint unsigned", nullable: false, defaultValue: (ushort)0),
                    counter3 = table.Column<ushort>(type: "smallint unsigned", nullable: false, defaultValue: (ushort)0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_queststatus", x => new { x.owner_guid, x.quest_id });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "character_spell",
                columns: table => new
                {
                    owner_guid = table.Column<uint>(type: "int unsigned", nullable: false),
                    spell = table.Column<uint>(type: "int unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_spell", x => new { x.owner_guid, x.spell });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "characters",
                columns: table => new
                {
                    guid = table.Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    account_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    name = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    race = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    @class = table.Column<byte>(name: "class", type: "tinyint unsigned", nullable: false),
                    gender = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    skin = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    face = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    hair_style = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    hair_color = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    facial_hair = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    level = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)1),
                    zone = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    map = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    position_x = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    position_y = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    position_z = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    created_at = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    money = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 1000000u),
                    xp = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    action_bars = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_characters", x => x.guid);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "realmlist",
                columns: table => new
                {
                    id = table.Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    address = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    port = table.Column<ushort>(type: "smallint unsigned", nullable: false, defaultValue: (ushort)8085),
                    type = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    flags = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    timezone = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)1),
                    population = table.Column<float>(type: "float", nullable: false, defaultValue: 0f)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_realmlist", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "uk_account_username",
                table: "account",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_aura_owner",
                table: "character_aura",
                column: "owner_guid");

            migrationBuilder.CreateIndex(
                name: "ix_items_owner",
                table: "character_items",
                column: "owner_guid");

            migrationBuilder.CreateIndex(
                name: "ix_qs_owner",
                table: "character_queststatus",
                column: "owner_guid");

            migrationBuilder.CreateIndex(
                name: "ix_spell_owner",
                table: "character_spell",
                column: "owner_guid");

            migrationBuilder.CreateIndex(
                name: "ix_characters_account",
                table: "characters",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "uk_characters_name",
                table: "characters",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_realmlist_name",
                table: "realmlist",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account");

            migrationBuilder.DropTable(
                name: "account_data");

            migrationBuilder.DropTable(
                name: "character_action");

            migrationBuilder.DropTable(
                name: "character_aura");

            migrationBuilder.DropTable(
                name: "character_declined_names");

            migrationBuilder.DropTable(
                name: "character_items");

            migrationBuilder.DropTable(
                name: "character_queststatus");

            migrationBuilder.DropTable(
                name: "character_spell");

            migrationBuilder.DropTable(
                name: "characters");

            migrationBuilder.DropTable(
                name: "realmlist");
        }
    }
}
