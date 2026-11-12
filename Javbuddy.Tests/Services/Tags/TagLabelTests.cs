using Javbuddy.Services.Tags;

namespace Javbuddy.Tests.Services.Tags;

public class TagLabelTests
{
    [Fact]
    public void Format_ReturnsTheName_ForATagWithoutAParent() => Assert.Equal("Long", TagLabel.Format(null, "Long"));

    [Fact]
    public void Format_PrefixesTheParent_ForASubtag() => Assert.Equal("Hair › Long", TagLabel.Format("Hair", "Long"));
}
