using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.Movies;

public class UncensoredCodeExtractionTests
{
    [Theory]
    [InlineData("cospuri-449", "cospuri-449")]
    [InlineData("carib-010120-001", "carib-010120-001")]
    [InlineData("1pondo-010120_001", "1pondo-010120_001")]
    [InlineData("[HD] 10musume-010120_001.mp4", "10musume-010120_001")]
    [InlineData("pacopacomama-010120_001 1080p", "pacopacomama-010120_001")]
    [InlineData("tokyo-hot-n1234", "tokyo-hot-n1234")]
    [InlineData("SSNI-081 [1080p]", "SSNI-081")]
    public void ExtractMovieCode_keeps_the_full_uncensored_studio_code(string text, string expected)
    {
        Assert.Equal(expected, CodeNormalization.ExtractMovieCode(text));
    }
}
