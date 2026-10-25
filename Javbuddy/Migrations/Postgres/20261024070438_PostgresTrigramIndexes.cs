using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations.Postgres;

/// <inheritdoc />
public partial class _20261024070438_PostgresTrigramIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:CollationDefinition:case_insensitive", "und-u-ks-level2,und-u-ks-level2,icu,False")
            .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
            .OldAnnotation("Npgsql:CollationDefinition:case_insensitive", "und-u-ks-level2,und-u-ks-level2,icu,False");

        migrationBuilder.AlterColumn<string>(
            name: "Code",
            table: "CachedImages",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            collation: "case_insensitive",
            oldClrType: typeof(string),
            oldType: "character varying(50)",
            oldMaxLength: 50);

        migrationBuilder.CreateIndex(
            name: "IX_Scenes_Title_Trgm",
            table: "Scenes",
            column: "Title")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

        migrationBuilder.CreateIndex(
            name: "IX_Movies_Code_Trgm",
            table: "Movies",
            column: "Code")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" })
            .Annotation("Relational:Collation", new[] { "default" });

        migrationBuilder.CreateIndex(
            name: "IX_Movies_MetaTitle_Trgm",
            table: "Movies",
            column: "MetaTitle")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

        migrationBuilder.CreateIndex(
            name: "IX_Movies_Title_Trgm",
            table: "Movies",
            column: "Title")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Scenes_Title_Trgm",
            table: "Scenes");

        migrationBuilder.DropIndex(
            name: "IX_Movies_Code_Trgm",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_MetaTitle_Trgm",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_Title_Trgm",
            table: "Movies");

        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:CollationDefinition:case_insensitive", "und-u-ks-level2,und-u-ks-level2,icu,False")
            .OldAnnotation("Npgsql:CollationDefinition:case_insensitive", "und-u-ks-level2,und-u-ks-level2,icu,False")
            .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

        migrationBuilder.AlterColumn<string>(
            name: "Code",
            table: "CachedImages",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(50)",
            oldMaxLength: 50,
            oldCollation: "case_insensitive");
    }
}
