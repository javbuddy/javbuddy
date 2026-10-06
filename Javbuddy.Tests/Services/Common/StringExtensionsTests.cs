using Javbuddy.Services.Common;

namespace Javbuddy.Tests.Services.Common;

public class StringExtensionsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\t\n", null)]
    [InlineData("abc", "abc")]
    [InlineData("  abc  ", "abc")]
    [InlineData(" a b ", "a b")]
    public void TrimToNull_TrimsAndMapsBlankToNull(string? input, string? expected) =>
        Assert.Equal(expected, input.TrimToNull());
}
