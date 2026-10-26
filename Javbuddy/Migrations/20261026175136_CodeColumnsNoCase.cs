using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261026175136_CodeColumnsNoCase : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "MovieCode",
            table: "TorrentDownloads",
            type: "TEXT",
            maxLength: 50,
            nullable: false,
            collation: "NOCASE",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 50);

        migrationBuilder.AlterColumn<string>(
            name: "Code",
            table: "DiscoveredMovieCandidates",
            type: "TEXT",
            maxLength: 50,
            nullable: false,
            collation: "NOCASE",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 50);

        migrationBuilder.AlterColumn<string>(
            name: "Code",
            table: "CachedImages",
            type: "TEXT",
            maxLength: 50,
            nullable: false,
            collation: "NOCASE",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 50);

        migrationBuilder.AlterColumn<string>(
            name: "SourceMovieCode",
            table: "ActorImages",
            type: "TEXT",
            maxLength: 50,
            nullable: false,
            collation: "NOCASE",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 50);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "MovieCode",
            table: "TorrentDownloads",
            type: "TEXT",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 50,
            oldCollation: "NOCASE");

        migrationBuilder.AlterColumn<string>(
            name: "Code",
            table: "DiscoveredMovieCandidates",
            type: "TEXT",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 50,
            oldCollation: "NOCASE");

        migrationBuilder.AlterColumn<string>(
            name: "Code",
            table: "CachedImages",
            type: "TEXT",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 50,
            oldCollation: "NOCASE");

        migrationBuilder.AlterColumn<string>(
            name: "SourceMovieCode",
            table: "ActorImages",
            type: "TEXT",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 50,
            oldCollation: "NOCASE");
    }
}
