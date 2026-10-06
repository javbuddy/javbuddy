using Javbuddy.Components.Shared;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Components.Shared;

public class PlayerClipTests
{
    private static SceneWallCard SceneCard(double? end) =>
        new(3, 30, "A/B 1", "Movie", "Scene 2", 60, end, [], [], false, null, null)
        {
            JellyfinItemId = "jf-1",
            JellyfinServerId = "srv",
            DurationSeconds = 1800,
        };

    private static HighlightWallCard HighlightCard() =>
        new(4, 30, "ABC-123", "Movie", "Climax", 90, 100.5, [], [], false, null, null)
        {
            JellyfinItemId = "jf-1",
            JellyfinServerId = "srv",
            DurationSeconds = 1800,
        };

    [Fact]
    public void FromScene_PlaysToItsEffectiveEnd_OrToTheEndOfTheFileWithoutOne()
    {
        Assert.Equal(
            new PlayerClip(PlayerClipKind.Scene, 3, 30, "A/B 1", "Scene 2", 60, 600, "jf-1", "srv", 1800),
            PlayerClip.FromScene(SceneCard(600)));
        Assert.Null(PlayerClip.FromScene(SceneCard(null)).EndSeconds);
    }

    [Fact]
    public void FromHighlight_PlaysItsRange()
    {
        Assert.Equal(
            new PlayerClip(PlayerClipKind.Highlight, 4, 30, "ABC-123", "Climax", 90, 100.5, "jf-1", "srv", 1800),
            PlayerClip.FromHighlight(HighlightCard()));
    }

    private static ApexWallCard ApexCard(double seconds) =>
        new(5, 30, "ABC-123", "Movie", "Aika — Squirt", seconds, [], [], false, null)
        {
            JellyfinItemId = "jf-1",
            JellyfinServerId = "srv",
            DurationSeconds = 1800,
        };

    [Fact]
    public void FromApex_PlaysTheSecondsAroundIt()
    {
        Assert.Equal(
            new PlayerClip(PlayerClipKind.Apex, 5, 30, "ABC-123", "Aika — Squirt", 95.5, 105.5, "jf-1", "srv", 1800),
            PlayerClip.FromApex(ApexCard(100.5)));
    }

    [Fact]
    public void FromApex_PlaysItsOwnWindow_CutAtTheMoviesEnd()
    {
        Assert.Equal((98.5, 120.5), (PlayerClip.FromApex(ApexCard(100.5) with { Window = new(2, 20) }) is var clip ? (clip.StartSeconds, clip.EndSeconds!.Value) : default));
        Assert.Equal(1800d, PlayerClip.FromApex(ApexCard(1795) with { Window = new(5, 30) }).EndSeconds);
    }

    [Fact]
    public void EditUrl_DeepLinksIntoMovieDetailsEditor_WithTheCodeEscaped()
    {
        Assert.Equal("/movies/A%2FB%201?scene=3", PlayerClip.FromScene(SceneCard(600)).EditUrl);
        Assert.Equal("/movies/ABC-123?highlight=4", PlayerClip.FromHighlight(HighlightCard()).EditUrl);
        Assert.Equal("/movies/ABC-123?t=95.5", PlayerClip.FromApex(ApexCard(100.5)).EditUrl);
    }
}
