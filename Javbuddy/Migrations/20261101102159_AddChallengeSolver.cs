using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261101102159_AddChallengeSolver : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "UseChallengeSolver",
            table: "WarashiSettings",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "UseChallengeSolver",
            table: "MinnanoAvSettings",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.CreateTable(
            name: "FlareSolverrSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                BaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                MaxTimeoutMs = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_FlareSolverrSettings", x => x.Id));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "FlareSolverrSettings");

        migrationBuilder.DropColumn(
            name: "UseChallengeSolver",
            table: "WarashiSettings");

        migrationBuilder.DropColumn(
            name: "UseChallengeSolver",
            table: "MinnanoAvSettings");
    }
}
