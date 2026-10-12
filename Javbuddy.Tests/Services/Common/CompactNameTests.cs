using Javbuddy.Services.Common;

namespace Javbuddy.Tests.Services.Common;

public class CompactNameTests
{
    [Theory]
    [InlineData("山田 花子", "山田花子")]
    [InlineData("山田　花子", "山田花子")]
    [InlineData("山田花子", "山田花子")]
    [InlineData(" Yui  Hatano ", "YuiHatano")]
    [InlineData("", "")]
    public void CompactName_StripsAsciiAndIdeographicSpaces(string input, string expected) =>
        Assert.Equal(expected, input.CompactName());
}
