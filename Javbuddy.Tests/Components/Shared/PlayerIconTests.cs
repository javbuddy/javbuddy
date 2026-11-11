using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class PlayerIconTests : BunitContext
{
    [Theory]
    [InlineData(PlayerIconName.Previous, 1)]
    [InlineData(PlayerIconName.Next, 1)]
    [InlineData(PlayerIconName.Play, 1)]
    [InlineData(PlayerIconName.Pause, 1)]
    [InlineData(PlayerIconName.Sound, 3)]
    [InlineData(PlayerIconName.Muted, 1)]
    [InlineData(PlayerIconName.FullscreenEnter, 1)]
    [InlineData(PlayerIconName.FullscreenExit, 1)]
    public void RendersAnAriaHiddenSvg_WithTheIconsPaths(PlayerIconName name, int pathCount)
    {
        var cut = Render<PlayerIcon>(p => p.Add(x => x.Name, name));

        var svg = cut.Find("svg");
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("0 0 16 16", svg.GetAttribute("viewBox"));
        Assert.Equal("currentColor", svg.GetAttribute("fill"));
        Assert.Equal(pathCount, cut.FindAll("svg > path").Count);
    }

    [Fact]
    public void EveryIconDrawsDistinctPaths()
    {
        var shapes = Enum.GetValues<PlayerIconName>()
            .Select(name => Render<PlayerIcon>(p => p.Add(x => x.Name, name)).Find("svg").InnerHtml)
            .ToList();

        Assert.Equal(shapes.Count, shapes.Distinct().Count());
    }

    [Theory]
    [InlineData(PlayerIconName.Play, "16")]
    [InlineData(PlayerIconName.Sound, "16")]
    [InlineData(PlayerIconName.FullscreenEnter, "14")]
    [InlineData(PlayerIconName.FullscreenExit, "14")]
    public void FullscreenIconsAreSmaller(PlayerIconName name, string size)
    {
        var svg = Render<PlayerIcon>(p => p.Add(x => x.Name, name)).Find("svg");

        Assert.Equal(size, svg.GetAttribute("width"));
        Assert.Equal(size, svg.GetAttribute("height"));
    }

    [Fact]
    public void Class_IsOnTheSvg_AndAbsentWithoutOne()
    {
        var withClass = Render<PlayerIcon>(p => p.Add(x => x.Name, PlayerIconName.Play).Add(x => x.Class, "scrub-bar-icon-play"));
        var without = Render<PlayerIcon>(p => p.Add(x => x.Name, PlayerIconName.Play));

        Assert.Equal("scrub-bar-icon-play", withClass.Find("svg").GetAttribute("class"));
        Assert.Null(without.Find("svg").GetAttribute("class"));
    }
}
