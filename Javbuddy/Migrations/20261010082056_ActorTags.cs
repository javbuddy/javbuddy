using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Javbuddy.Migrations;

/// <inheritdoc />
public partial class _20261010082056_ActorTags : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsActorTag",
            table: "Tags",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "ApexActorTags",
            columns: table => new
            {
                ApexId = table.Column<int>(type: "INTEGER", nullable: false),
                ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApexActorTags", x => new { x.ApexId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_ApexActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexActorTags_MovieApexes_ApexId",
                    column: x => x.ApexId,
                    principalTable: "MovieApexes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ApexEffectiveActorTags",
            columns: table => new
            {
                ApexId = table.Column<int>(type: "INTEGER", nullable: false),
                ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApexEffectiveActorTags", x => new { x.ApexId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_ApexEffectiveActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexEffectiveActorTags_MovieApexes_ApexId",
                    column: x => x.ApexId,
                    principalTable: "MovieApexes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ApexEffectiveActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "HighlightActorTags",
            columns: table => new
            {
                HighlightId = table.Column<int>(type: "INTEGER", nullable: false),
                ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HighlightActorTags", x => new { x.HighlightId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_HighlightActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightActorTags_MovieHighlights_HighlightId",
                    column: x => x.HighlightId,
                    principalTable: "MovieHighlights",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "HighlightEffectiveActorTags",
            columns: table => new
            {
                HighlightId = table.Column<int>(type: "INTEGER", nullable: false),
                ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                IsRolledUp = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HighlightEffectiveActorTags", x => new { x.HighlightId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_HighlightEffectiveActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightEffectiveActorTags_MovieHighlights_HighlightId",
                    column: x => x.HighlightId,
                    principalTable: "MovieHighlights",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_HighlightEffectiveActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MovieActorTags",
            columns: table => new
            {
                MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MovieActorTags", x => new { x.MovieId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_MovieActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_MovieActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SceneActorTags",
            columns: table => new
            {
                SceneId = table.Column<int>(type: "INTEGER", nullable: false),
                ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SceneActorTags", x => new { x.SceneId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_SceneActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneActorTags_Scenes_SceneId",
                    column: x => x.SceneId,
                    principalTable: "Scenes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SceneEffectiveActorTags",
            columns: table => new
            {
                SceneId = table.Column<int>(type: "INTEGER", nullable: false),
                ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                IsRolledUp = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SceneEffectiveActorTags", x => new { x.SceneId, x.ActorId, x.TagId });
                table.ForeignKey(
                    name: "FK_SceneEffectiveActorTags_MovieActors_MovieId_ActorId",
                    columns: x => new { x.MovieId, x.ActorId },
                    principalTable: "MovieActors",
                    principalColumns: new[] { "MovieId", "ActorId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneEffectiveActorTags_Scenes_SceneId",
                    column: x => x.SceneId,
                    principalTable: "Scenes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SceneEffectiveActorTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ApexActorTags_MovieId_ActorId",
            table: "ApexActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_ApexActorTags_TagId_ActorId_ApexId",
            table: "ApexActorTags",
            columns: new[] { "TagId", "ActorId", "ApexId" });

        migrationBuilder.CreateIndex(
            name: "IX_ApexEffectiveActorTags_MovieId_ActorId",
            table: "ApexEffectiveActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_ApexEffectiveActorTags_TagId_ActorId_ApexId",
            table: "ApexEffectiveActorTags",
            columns: new[] { "TagId", "ActorId", "ApexId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightActorTags_MovieId_ActorId",
            table: "HighlightActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightActorTags_TagId_ActorId_HighlightId",
            table: "HighlightActorTags",
            columns: new[] { "TagId", "ActorId", "HighlightId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightEffectiveActorTags_MovieId_ActorId",
            table: "HighlightEffectiveActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_HighlightEffectiveActorTags_TagId_ActorId_HighlightId",
            table: "HighlightEffectiveActorTags",
            columns: new[] { "TagId", "ActorId", "HighlightId" });

        migrationBuilder.CreateIndex(
            name: "IX_MovieActorTags_TagId_ActorId_MovieId",
            table: "MovieActorTags",
            columns: new[] { "TagId", "ActorId", "MovieId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneActorTags_MovieId_ActorId",
            table: "SceneActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneActorTags_TagId_ActorId_SceneId",
            table: "SceneActorTags",
            columns: new[] { "TagId", "ActorId", "SceneId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneEffectiveActorTags_MovieId_ActorId",
            table: "SceneEffectiveActorTags",
            columns: new[] { "MovieId", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_SceneEffectiveActorTags_TagId_ActorId_SceneId",
            table: "SceneEffectiveActorTags",
            columns: new[] { "TagId", "ActorId", "SceneId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ApexActorTags");

        migrationBuilder.DropTable(
            name: "ApexEffectiveActorTags");

        migrationBuilder.DropTable(
            name: "HighlightActorTags");

        migrationBuilder.DropTable(
            name: "HighlightEffectiveActorTags");

        migrationBuilder.DropTable(
            name: "MovieActorTags");

        migrationBuilder.DropTable(
            name: "SceneActorTags");

        migrationBuilder.DropTable(
            name: "SceneEffectiveActorTags");

        migrationBuilder.DropColumn(
            name: "IsActorTag",
            table: "Tags");
    }
}
