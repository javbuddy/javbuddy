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

    [Theory]
    [InlineData("carib-010120-001", true)]
    [InlineData("1pondo-010120_001", true)]
    [InlineData("tokyo-hot-n1234", true)]
    [InlineData("  heyzo-prefix-carib-010120-001", false)]
    [InlineData("carib-010120-001 [1080p]", false)]
    [InlineData("cospuri-449", false)]
    [InlineData("SSNI-081", false)]
    [InlineData("PACO-123", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsUncensoredStudioCode_OnlyTrueForAWholeDateCodedStudioCode(string? code, bool expected)
    {
        Assert.Equal(expected, CodeNormalization.IsUncensoredStudioCode(code));
    }

    [Theory]
    [InlineData("carib-010120-001", Javbuddy.Models.MovieContentType.Uncensored)]
    [InlineData("SSNI-081", Javbuddy.Models.MovieContentType.Censored)]
    public void ContentTypeFor_DefaultsToCensoredAndDetectsDateCodedUncensoredCodes(string code, Javbuddy.Models.MovieContentType expected)
    {
        Assert.Equal(expected, MovieAddService.ContentTypeFor(code));
    }
}
