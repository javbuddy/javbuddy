using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261115143215_AddActorPlaceholder : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "PlaceholderMovieId",
            table: "Actors",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Actors_PlaceholderMovieId",
            table: "Actors",
            column: "PlaceholderMovieId",
            filter: "\"PlaceholderMovieId\" IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "FK_Actors_Movies_PlaceholderMovieId",
            table: "Actors",
            column: "PlaceholderMovieId",
            principalTable: "Movies",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Actors_Movies_PlaceholderMovieId",
            table: "Actors");

        migrationBuilder.DropIndex(
            name: "IX_Actors_PlaceholderMovieId",
            table: "Actors");

        migrationBuilder.DropColumn(
            name: "PlaceholderMovieId",
            table: "Actors");
    }
}
