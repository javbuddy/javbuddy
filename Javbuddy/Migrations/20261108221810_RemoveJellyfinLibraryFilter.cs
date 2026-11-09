using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261108221810_RemoveJellyfinLibraryFilter : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Movies_JellyfinLibraryName",
            table: "Movies");

        migrationBuilder.DropColumn(
            name: "JellyfinFallback",
            table: "TrickplaySettings");

        migrationBuilder.DropColumn(
            name: "JellyfinLibraryId",
            table: "Movies");

        migrationBuilder.DropColumn(
            name: "JellyfinLibraryName",
            table: "Movies");

        migrationBuilder.DropColumn(
            name: "SelectedLibraryNames",
            table: "JellyfinSettings");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "JellyfinFallback",
            table: "TrickplaySettings",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "JellyfinLibraryId",
            table: "Movies",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "JellyfinLibraryName",
            table: "Movies",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SelectedLibraryNames",
            table: "JellyfinSettings",
            type: "TEXT",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Movies_JellyfinLibraryName",
            table: "Movies",
            column: "JellyfinLibraryName");
    }
}
