using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261101092153_AddBackupSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BackupSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                RetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                FrequencyDays = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_BackupSettings", x => x.Id));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BackupSettings");
    }
}
