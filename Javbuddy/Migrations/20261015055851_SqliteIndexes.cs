using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261015055851_SqliteIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DateAdded",
            table: "Movies",
            type: "TEXT",
            nullable: false,
            computedColumnSql: "COALESCE(\"FileAddedAt\", \"CreatedAt\")",
            stored: false);

        migrationBuilder.CreateIndex(
            name: "IX_ScheduledTaskRuns_TaskName_EndedAt",
            table: "ScheduledTaskRuns",
            columns: new[] { "TaskName", "EndedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Movies_ClipActorsStale",
            table: "Movies",
            column: "ClipActorsStale",
            filter: "\"ClipActorsStale\"");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_DateAdded",
            table: "Movies",
            column: "DateAdded");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_JellyfinLibraryName",
            table: "Movies",
            column: "JellyfinLibraryName");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_NfoDriftKind",
            table: "Movies",
            column: "NfoDriftKind");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_Status_DateAdded",
            table: "Movies",
            columns: new[] { "Status", "DateAdded" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ScheduledTaskRuns_TaskName_EndedAt",
            table: "ScheduledTaskRuns");

        migrationBuilder.DropIndex(
            name: "IX_Movies_ClipActorsStale",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_DateAdded",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_JellyfinLibraryName",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_NfoDriftKind",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_Status_DateAdded",
            table: "Movies");

        migrationBuilder.DropColumn(
            name: "DateAdded",
            table: "Movies");
    }
}
