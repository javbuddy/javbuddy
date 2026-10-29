using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261029080602_NameCollationsAndIndexCleanup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The NOCASE unique indexes below would fail to build on a case-only duplicate. Tags and actors keep every row,
        // renamed with a " (2)" suffix for the user to merge; a duplicate alias of the same actor says nothing new.
        migrationBuilder.Sql("""
            WITH ranked AS (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "Name" COLLATE NOCASE ORDER BY "Id") AS rn FROM "Tags")
            UPDATE "Tags" SET "Name" = "Name" || ' (' || ranked.rn || ')' FROM ranked WHERE ranked."Id" = "Tags"."Id" AND ranked.rn > 1;
            """);
        migrationBuilder.Sql("""
            WITH ranked AS (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "FirstName" COLLATE NOCASE, "LastName" COLLATE NOCASE ORDER BY "Id") AS rn FROM "Actors")
            UPDATE "Actors" SET "FirstName" = "FirstName" || ' (' || ranked.rn || ')' FROM ranked WHERE ranked."Id" = "Actors"."Id" AND ranked.rn > 1;
            """);
        migrationBuilder.Sql("""
            DELETE FROM "ActorAliases" WHERE EXISTS (SELECT 1 FROM "ActorAliases" AS o
                WHERE o."ActorId" = "ActorAliases"."ActorId" AND o."Name" = "ActorAliases"."Name" COLLATE NOCASE AND o."Id" < "ActorAliases"."Id");
            """);

        migrationBuilder.DropIndex(
            name: "IX_TorrentDownloads_Hash",
            table: "TorrentDownloads");

        migrationBuilder.DropIndex(
            name: "IX_Scenes_IsFavorite",
            table: "Scenes");

        migrationBuilder.DropIndex(
            name: "IX_Movies_IsFavorite",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_DeletedMovies_CanonicalKey",
            table: "DeletedMovies");

        migrationBuilder.DropIndex(
            name: "IX_Actors_IsFavorite",
            table: "Actors");

        migrationBuilder.DropIndex(
            name: "IX_Actors_IsRetired",
            table: "Actors");

        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "Tags",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            collation: "NOCASE",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200);

        migrationBuilder.AlterColumn<string>(
            name: "LastName",
            table: "Actors",
            type: "TEXT",
            maxLength: 200,
            nullable: true,
            collation: "NOCASE",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "FirstName",
            table: "Actors",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            collation: "NOCASE",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200);

        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "ActorAliases",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            collation: "NOCASE",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200);

        migrationBuilder.CreateIndex(
            name: "IX_Scenes_IsFavorite",
            table: "Scenes",
            column: "IsFavorite",
            filter: "\"IsFavorite\"");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_IsFavorite",
            table: "Movies",
            column: "IsFavorite",
            filter: "\"IsFavorite\"");

        migrationBuilder.CreateIndex(
            name: "IX_Actors_FirstName_SingleName",
            table: "Actors",
            column: "FirstName",
            unique: true,
            filter: "\"LastName\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Actors_IsFavorite",
            table: "Actors",
            column: "IsFavorite",
            filter: "\"IsFavorite\"");

        migrationBuilder.CreateIndex(
            name: "IX_Actors_IsRetired",
            table: "Actors",
            column: "IsRetired",
            filter: "\"IsRetired\"");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Scenes_IsFavorite",
            table: "Scenes");

        migrationBuilder.DropIndex(
            name: "IX_Movies_IsFavorite",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Actors_FirstName_SingleName",
            table: "Actors");

        migrationBuilder.DropIndex(
            name: "IX_Actors_IsFavorite",
            table: "Actors");

        migrationBuilder.DropIndex(
            name: "IX_Actors_IsRetired",
            table: "Actors");

        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "Tags",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200,
            oldCollation: "NOCASE");

        migrationBuilder.AlterColumn<string>(
            name: "LastName",
            table: "Actors",
            type: "TEXT",
            maxLength: 200,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200,
            oldNullable: true,
            oldCollation: "NOCASE");

        migrationBuilder.AlterColumn<string>(
            name: "FirstName",
            table: "Actors",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200,
            oldCollation: "NOCASE");

        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "ActorAliases",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200,
            oldCollation: "NOCASE");

        migrationBuilder.CreateIndex(
            name: "IX_TorrentDownloads_Hash",
            table: "TorrentDownloads",
            column: "Hash");

        migrationBuilder.CreateIndex(
            name: "IX_Scenes_IsFavorite",
            table: "Scenes",
            column: "IsFavorite");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_IsFavorite",
            table: "Movies",
            column: "IsFavorite");

        migrationBuilder.CreateIndex(
            name: "IX_DeletedMovies_CanonicalKey",
            table: "DeletedMovies",
            column: "CanonicalKey");

        migrationBuilder.CreateIndex(
            name: "IX_Actors_IsFavorite",
            table: "Actors",
            column: "IsFavorite");

        migrationBuilder.CreateIndex(
            name: "IX_Actors_IsRetired",
            table: "Actors",
            column: "IsRetired");
    }
}
