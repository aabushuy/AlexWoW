using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexWoW.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTalentResetCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "talent_reset_cost",
                table: "characters",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "talent_reset_cost",
                table: "characters");
        }
    }
}
