using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexWoW.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterIsTester : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_tester",
                table: "characters",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_tester",
                table: "characters");
        }
    }
}
