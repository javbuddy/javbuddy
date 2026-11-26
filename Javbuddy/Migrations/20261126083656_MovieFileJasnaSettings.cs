using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261126083656_MovieFileJasnaSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "JasnaRestoration",
            table: "MovieFiles",
            type: "TEXT",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "JasnaSettingsJson",
            table: "MovieFiles",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "JasnaVersion",
            table: "MovieFiles",
            type: "TEXT",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "MediaProbeRevision",
            table: "MovieFiles",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "JasnaRestoration",
            table: "MovieFiles");

        migrationBuilder.DropColumn(
            name: "JasnaSettingsJson",
            table: "MovieFiles");

        migrationBuilder.DropColumn(
            name: "JasnaVersion",
            table: "MovieFiles");

        migrationBuilder.DropColumn(
            name: "MediaProbeRevision",
            table: "MovieFiles");
    }
}
