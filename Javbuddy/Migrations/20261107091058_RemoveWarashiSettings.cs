using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261107091058_RemoveWarashiSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "WarashiSettings");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarashiSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                BaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                LastEnrichSummary = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                LastEnrichedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                OverwriteExisting = table.Column<bool>(type: "INTEGER", nullable: false),
                RequestDelayMs = table.Column<int>(type: "INTEGER", nullable: false),
                UseChallengeSolver = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_WarashiSettings", x => x.Id));
    }
}
