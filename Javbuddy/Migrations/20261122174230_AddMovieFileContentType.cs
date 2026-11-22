using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261122174230_AddMovieFileContentType : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ContentType",
            table: "MovieFiles",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        // Existing versions start with their movie's content type, so the primary version's mirror doesn't flip it.
        migrationBuilder.Sql("UPDATE MovieFiles SET ContentType = (SELECT m.ContentType FROM Movies m WHERE m.Id = MovieFiles.MovieId);");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ContentType",
            table: "MovieFiles");
    }
}
