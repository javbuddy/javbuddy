using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261123163437_AddTagPropagation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "PropagatesDown",
            table: "SceneTags",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "PropagatesDown",
            table: "MovieTags",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "PropagatesDown",
            table: "HighlightTags",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "ApexInheritedTags",
            columns: table => new
            {
                ApexId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApexInheritedTags", x => new { x.ApexId, x.TagId });
                table.ForeignKey(
                    name: "FK_ApexInheritedTags_MovieApexes_ApexId",
                    column: x => x.ApexId,
                    principalTable: "MovieApexes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexInheritedTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ApexTagExclusions",
            columns: table => new
            {
                ApexId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApexTagExclusions", x => new { x.ApexId, x.TagId });
                table.ForeignKey(
                    name: "FK_ApexTagExclusions_MovieApexes_ApexId",
                    column: x => x.ApexId,
                    principalTable: "MovieApexes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexTagExclusions_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "HighlightInheritedTags",
            columns: table => new
            {
                HighlightId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HighlightInheritedTags", x => new { x.HighlightId, x.TagId });
                table.ForeignKey(
                    name: "FK_HighlightInheritedTags_MovieHighlights_HighlightId",
                    column: x => x.HighlightId,
                    principalTable: "MovieHighlights",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightInheritedTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "HighlightTagExclusions",
            columns: table => new
            {
                HighlightId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HighlightTagExclusions", x => new { x.HighlightId, x.TagId });
                table.ForeignKey(
                    name: "FK_HighlightTagExclusions_MovieHighlights_HighlightId",
                    column: x => x.HighlightId,
                    principalTable: "MovieHighlights",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightTagExclusions_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SceneInheritedTags",
            columns: table => new
            {
                SceneId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SceneInheritedTags", x => new { x.SceneId, x.TagId });
                table.ForeignKey(
                    name: "FK_SceneInheritedTags_Scenes_SceneId",
                    column: x => x.SceneId,
                    principalTable: "Scenes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneInheritedTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SceneTagExclusions",
            columns: table => new
            {
                SceneId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SceneTagExclusions", x => new { x.SceneId, x.TagId });
                table.ForeignKey(
                    name: "FK_SceneTagExclusions_Scenes_SceneId",
                    column: x => x.SceneId,
                    principalTable: "Scenes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneTagExclusions_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ApexInheritedTags_MovieId",
            table: "ApexInheritedTags",
            column: "MovieId");

        migrationBuilder.CreateIndex(
            name: "IX_ApexInheritedTags_TagId_ApexId",
            table: "ApexInheritedTags",
            columns: new[] { "TagId", "ApexId" });

        migrationBuilder.CreateIndex(
            name: "IX_ApexTagExclusions_MovieId",
            table: "ApexTagExclusions",
            column: "MovieId");

        migrationBuilder.CreateIndex(
            name: "IX_ApexTagExclusions_TagId_ApexId",
            table: "ApexTagExclusions",
            columns: new[] { "TagId", "ApexId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightInheritedTags_MovieId",
            table: "HighlightInheritedTags",
            column: "MovieId");

        migrationBuilder.CreateIndex(
            name: "IX_HighlightInheritedTags_TagId_HighlightId",
            table: "HighlightInheritedTags",
            columns: new[] { "TagId", "HighlightId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightTagExclusions_MovieId",
            table: "HighlightTagExclusions",
            column: "MovieId");

        migrationBuilder.CreateIndex(
            name: "IX_HighlightTagExclusions_TagId_HighlightId",
            table: "HighlightTagExclusions",
            columns: new[] { "TagId", "HighlightId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneInheritedTags_MovieId",
            table: "SceneInheritedTags",
            column: "MovieId");

        migrationBuilder.CreateIndex(
            name: "IX_SceneInheritedTags_TagId_SceneId",
            table: "SceneInheritedTags",
            columns: new[] { "TagId", "SceneId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneTagExclusions_MovieId",
            table: "SceneTagExclusions",
            column: "MovieId");

        migrationBuilder.CreateIndex(
            name: "IX_SceneTagExclusions_TagId_SceneId",
            table: "SceneTagExclusions",
            columns: new[] { "TagId", "SceneId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ApexInheritedTags");

        migrationBuilder.DropTable(
            name: "ApexTagExclusions");

        migrationBuilder.DropTable(
            name: "HighlightInheritedTags");

        migrationBuilder.DropTable(
            name: "HighlightTagExclusions");

        migrationBuilder.DropTable(
            name: "SceneInheritedTags");

        migrationBuilder.DropTable(
            name: "SceneTagExclusions");

        migrationBuilder.DropColumn(
            name: "PropagatesDown",
            table: "SceneTags");

        migrationBuilder.DropColumn(
            name: "PropagatesDown",
            table: "MovieTags");

        migrationBuilder.DropColumn(
            name: "PropagatesDown",
            table: "HighlightTags");
    }
}
