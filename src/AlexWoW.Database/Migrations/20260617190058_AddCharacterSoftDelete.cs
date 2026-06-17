using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexWoW.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uk_characters_name",
                table: "characters");

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "characters",
                type: "timestamp",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uk_characters_name",
                table: "characters",
                columns: new[] { "name", "deleted_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uk_characters_name",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "characters");

            migrationBuilder.CreateIndex(
                name: "uk_characters_name",
                table: "characters",
                column: "name",
                unique: true);
        }
    }
}
