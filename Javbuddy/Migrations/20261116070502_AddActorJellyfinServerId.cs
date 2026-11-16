using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261116070502_AddActorJellyfinServerId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "JellyfinServerId",
            table: "Actors",
            type: "TEXT",
            maxLength: 200,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "JellyfinServerId",
            table: "Actors");
    }
}
