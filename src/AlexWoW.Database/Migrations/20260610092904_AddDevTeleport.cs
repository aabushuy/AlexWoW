using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexWoW.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDevTeleport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dev_teleport",
                columns: table => new
                {
                    id = table.Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    sort_order = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    faction = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    map = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    zone = table.Column<uint>(type: "int unsigned", nullable: false, defaultValue: 0u),
                    x = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    y = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    z = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    o = table.Column<float>(type: "float", nullable: false, defaultValue: 0f)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dev_teleport", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Сид столиц (faction: 1=Альянс, 2=Орда, 0=нейтрал). Координаты канонические для WotLK 3.3.5a;
            // редактируются в рантайме (на то и dev-таблица). Карты: 0=Вост.королевства, 1=Калимдор,
            // 530=Запределье, 571=Нордскол.
            migrationBuilder.InsertData(
                table: "dev_teleport",
                columns: new[] { "id", "sort_order", "name", "faction", "map", "zone", "x", "y", "z", "o" },
                values: new object[,]
                {
                    {  1u, 10, "Штормград",     (byte)1,   0u, 1519u, -8833.38f,   628.628f,   94.0066f,  1.06f  },
                    {  2u, 11, "Стальгорн",     (byte)1,   0u, 1537u, -4981.25f,  -881.542f,  502.660f,  5.40f  },
                    {  3u, 12, "Дарнас",        (byte)1,   1u, 1657u,  9949.71f,  2412.360f, 1331.610f,  4.71f  },
                    {  4u, 13, "Экзодар",       (byte)1, 530u, 3557u, -3965.70f,-11653.500f, -138.500f,  0.00f  },
                    {  5u, 20, "Оргриммар",     (byte)2,   1u, 1637u,  1629.36f, -4373.390f,   31.260f,  0.00f  },
                    {  6u, 21, "Громовой Утёс", (byte)2,   1u, 1638u, -1277.37f,   124.804f,  131.287f,  5.15f  },
                    {  7u, 22, "Подгород",      (byte)2,   0u, 1497u,  1633.75f,   240.167f,  -43.100f,  6.26f  },
                    {  8u, 23, "Луносвет",      (byte)2, 530u, 3487u,  9487.00f, -7279.300f,   14.300f,  0.00f  },
                    {  9u, 30, "Даларан",       (byte)0, 571u, 4395u,  5807.21f,   588.328f,  661.070f,  0.00f  },
                    { 10u, 31, "Шаттрат",       (byte)0, 530u, 3703u, -1838.16f,  5301.790f,  -12.428f,  5.94f  },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dev_teleport");
        }
    }
}
