using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class FavoriteHeartIconTests : BunitContext
{
    [Theory]
    [InlineData(false, "#jb-icon-heart")]
    [InlineData(true, "#jb-icon-heart-filled")]
    public void ReferencesTheSharedSymbol_InsteadOfInliningItsPath(bool filled, string symbol)
    {
        var cut = Render<FavoriteHeartIcon>(p => p.Add(c => c.Filled, filled).Add(c => c.Size, 15));

        Assert.Equal(symbol, cut.Find("svg > use").GetAttribute("href"));
        Assert.Equal("15", cut.Find("svg").GetAttribute("width"));
        Assert.Empty(cut.FindAll("path"));
    }
}
