using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations.Postgres;

/// <inheritdoc />
public partial class _20261023200651_UnlimitedCandidateJsonLists : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "GalleryImageUrls",
            table: "DiscoveredMovieCandidates",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(8000)",
            oldMaxLength: 8000);

        migrationBuilder.AlterColumn<string>(
            name: "ActressNames",
            table: "DiscoveredMovieCandidates",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(500)",
            oldMaxLength: 500);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "GalleryImageUrls",
            table: "DiscoveredMovieCandidates",
            type: "character varying(8000)",
            maxLength: 8000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "ActressNames",
            table: "DiscoveredMovieCandidates",
            type: "character varying(500)",
            maxLength: 500,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");
    }
}
