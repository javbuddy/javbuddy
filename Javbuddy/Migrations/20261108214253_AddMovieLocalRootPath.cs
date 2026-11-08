using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261108214253_AddMovieLocalRootPath : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LocalRootPath",
            table: "Movies",
            type: "TEXT",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Movies_LocalRootPath",
            table: "Movies",
            column: "LocalRootPath");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Movies_LocalRootPath",
            table: "Movies");

        migrationBuilder.DropColumn(
            name: "LocalRootPath",
            table: "Movies");
    }
}
