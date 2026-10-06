using Javbuddy.Services.Tags;

namespace Javbuddy.Tests.Services.Tags;

public class TagScriptDetectorTests
{
    [Theory]
    [InlineData("VR")]
    [InlineData("Solowork")]
    [InlineData("Café")]
    [InlineData("123")]
    [InlineData("")]
    public void ContainsNonLatinScript_ReturnsFalse_ForLatinOrNonLetterText(string value)
    {
        Assert.False(TagScriptDetector.ContainsNonLatinScript(value));
    }

    [Theory]
    [InlineData("単体作品")]
    [InlineData("ソロ")]
    [InlineData("한국어")]
    [InlineData("Русский")]
    public void ContainsNonLatinScript_ReturnsTrue_ForNonLatinScripts(string value)
    {
        Assert.True(TagScriptDetector.ContainsNonLatinScript(value));
    }
}
