using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261026175136_CodeColumnsNoCase : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The NOCASE unique indexes rebuilt below would fail on rows whose codes differ only by case (image cache keys
        // use the un-normalized route value), and Migrate() would then throw at startup. Both tables are disposable
        // cache/candidate data, so each later case-duplicate is dropped and the first row is kept.
        migrationBuilder.Sql("""
            DELETE FROM "CachedImages" WHERE EXISTS (SELECT 1 FROM "CachedImages" AS o
                WHERE o."Code" = "CachedImages"."Code" COLLATE NOCASE AND o."Role" = "CachedImages"."Role"
                AND o."Index" = "CachedImages"."Index" AND o."Variant" = "CachedImages"."Variant" AND o."Id" < "CachedImages"."Id");
            """);
        migrationBuilder.Sql("""
            DELETE FROM "DiscoveredMovieCandidates" WHERE EXISTS (SELECT 1 FROM "DiscoveredMovieCandidates" AS o
                WHERE o."Code" = "DiscoveredMovieCandidates"."Code" COLLATE NOCASE AND o."Id" < "DiscoveredMovieCandidates"."Id");
            """);

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
