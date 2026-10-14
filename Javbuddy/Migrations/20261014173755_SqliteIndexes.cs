using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261014173755_SqliteIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_ScheduledTaskRuns_TaskName_QueuedAt",
            table: "ScheduledTaskRuns",
            columns: new[] { "TaskName", "QueuedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Movies_ClipActorsStale",
            table: "Movies",
            column: "ClipActorsStale");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_CreatedAt",
            table: "Movies",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_FileAddedAt",
            table: "Movies",
            column: "FileAddedAt");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_JellyfinLibraryName",
            table: "Movies",
            column: "JellyfinLibraryName");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_MetaReleaseDate",
            table: "Movies",
            column: "MetaReleaseDate");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_NfoDriftKind",
            table: "Movies",
            column: "NfoDriftKind");

        migrationBuilder.CreateIndex(
            name: "IX_Movies_Status",
            table: "Movies",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ScheduledTaskRuns_TaskName_QueuedAt",
            table: "ScheduledTaskRuns");

        migrationBuilder.DropIndex(
            name: "IX_Movies_ClipActorsStale",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_CreatedAt",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_FileAddedAt",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_JellyfinLibraryName",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_MetaReleaseDate",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_NfoDriftKind",
            table: "Movies");

        migrationBuilder.DropIndex(
            name: "IX_Movies_Status",
            table: "Movies");
    }
}
