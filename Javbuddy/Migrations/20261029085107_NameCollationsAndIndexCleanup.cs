using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261029085107_NameCollationsAndIndexCleanup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The NOCASE unique indexes below would fail to build on a case-only duplicate. Tags and actors keep every row:
        // each later duplicate gets the first " (n)" suffix no other name has, ignoring case (each pick sees the renames
        // before it), for the user to merge. A duplicate alias of the same actor says nothing new.
        migrationBuilder.Sql("""
            UPDATE "Tags" SET "Name" = (
                WITH RECURSIVE n(i) AS (SELECT 2 UNION ALL SELECT i + 1 FROM n)
                SELECT "Tags"."Name" || ' (' || i || ')' FROM n
                WHERE NOT EXISTS (SELECT 1 FROM "Tags" AS t WHERE t."Name" = "Tags"."Name" || ' (' || i || ')' COLLATE NOCASE)
                LIMIT 1)
            WHERE EXISTS (SELECT 1 FROM "Tags" AS o WHERE o."Name" = "Tags"."Name" COLLATE NOCASE AND o."Id" < "Tags"."Id");
            """);
        // Cast linking matches names and aliases, so a renamed actor first keeps its old name ("Last First" and "First
        // Last") as an alias: otherwise the next cast sync would drop its movie links, and everything on them, before
        // the user gets to merge it.
        migrationBuilder.Sql("""
            INSERT INTO "ActorAliases" ("ActorId", "Name", "CreatedAt")
            SELECT n."Id", n."Name", datetime('now') FROM (
                SELECT "Id", CASE WHEN "LastName" IS NULL THEN "FirstName" ELSE "LastName" || ' ' || "FirstName" END AS "Name" FROM "Actors"
                UNION SELECT "Id", "FirstName" || ' ' || "LastName" FROM "Actors" WHERE "LastName" IS NOT NULL) AS n
            JOIN "Actors" AS a ON a."Id" = n."Id"
            WHERE EXISTS (SELECT 1 FROM "Actors" AS o WHERE o."FirstName" = a."FirstName" COLLATE NOCASE
                    AND (o."LastName" = a."LastName" COLLATE NOCASE OR (o."LastName" IS NULL AND a."LastName" IS NULL))
                    AND o."Id" < a."Id")
                AND NOT EXISTS (SELECT 1 FROM "ActorAliases" AS al WHERE al."ActorId" = n."Id" AND al."Name" = n."Name" COLLATE NOCASE);
            """);
        migrationBuilder.Sql("""
            UPDATE "Actors" SET "FirstName" = (
                WITH RECURSIVE n(i) AS (SELECT 2 UNION ALL SELECT i + 1 FROM n)
                SELECT "Actors"."FirstName" || ' (' || i || ')' FROM n
                WHERE NOT EXISTS (SELECT 1 FROM "Actors" AS a WHERE a."FirstName" = "Actors"."FirstName" || ' (' || i || ')' COLLATE NOCASE
                    AND (a."LastName" = "Actors"."LastName" COLLATE NOCASE OR (a."LastName" IS NULL AND "Actors"."LastName" IS NULL)))
                LIMIT 1)
            WHERE EXISTS (SELECT 1 FROM "Actors" AS o WHERE o."FirstName" = "Actors"."FirstName" COLLATE NOCASE
                AND (o."LastName" = "Actors"."LastName" COLLATE NOCASE OR (o."LastName" IS NULL AND "Actors"."LastName" IS NULL))
                AND o."Id" < "Actors"."Id");
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
            name: "IX_Actors_IsFavorite",
            table: "Actors",
            column: "IsFavorite");

        migrationBuilder.CreateIndex(
            name: "IX_Actors_IsRetired",
            table: "Actors",
            column: "IsRetired");
    }
}
