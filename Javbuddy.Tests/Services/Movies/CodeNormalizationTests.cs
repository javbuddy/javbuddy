using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.Movies;

public class CodeNormalizationTests
{
    [Theory]
    [InlineData("sivr-505", "SIVR505")]
    [InlineData("SIVR-505", "SIVR505")]
    [InlineData(" SIVR 505 ", "SIVR505")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_UppercasesAndStripsHyphensAndWhitespace(string? input, string expected)
    {
        Assert.Equal(expected, CodeNormalization.Normalize(input));
    }

    [Fact]
    public void GetCanonicalKey_PlainHyphenatedCode_IsUnchanged()
    {
        Assert.Equal("OFJE-600", CodeNormalization.GetCanonicalKey("OFJE-600"));
    }

    [Theory]
    [InlineData("9OFJE-600")]
    [InlineData("OFJE-600DOD")]
    [InlineData("OFJE-600BOD")]
    [InlineData("OFJE600")]
    public void GetCanonicalKey_DistributorVariants_CanonicalizeToTheBaseCode(string variant)
    {
        Assert.Equal("OFJE-600", CodeNormalization.GetCanonicalKey(variant));
    }

    [Fact]
    public void GetCanonicalKey_AlphanumericMakerCode_InsertsHyphen()
    {
        Assert.Equal("T28-585", CodeNormalization.GetCanonicalKey("T28-585"));
        Assert.Equal("T28-585", CodeNormalization.GetCanonicalKey("T28585"));
    }

    [Fact]
    public void GetCanonicalKey_RedundantZeroPadding_IsStripped()
    {
        Assert.Equal(CodeNormalization.GetCanonicalKey("ARMD-710"), CodeNormalization.GetCanonicalKey("ARMD-000710"));
    }

    [Fact]
    public void GetCanonicalKey_UnhyphenatedCode_MatchesHyphenatedForm()
    {
        Assert.Equal(CodeNormalization.GetCanonicalKey("REBD-173"), CodeNormalization.GetCanonicalKey("REBD173"));
        Assert.Equal(CodeNormalization.GetCanonicalKey("DSTH-7014"), CodeNormalization.GetCanonicalKey("DSTH7014"));
    }

    [Fact]
    public void GetCanonicalKey_BlankInput_ReturnsEmpty()
    {
        Assert.Equal("", CodeNormalization.GetCanonicalKey(null));
        Assert.Equal("", CodeNormalization.GetCanonicalKey(""));
    }

    [Fact]
    public void ScoreDvdIdForCanonical_PrefersPlainHyphenatedOverDistributorPrefixed()
    {
        var plain = CodeNormalization.ScoreDvdIdForCanonical("OFJE-600");
        var withDigitPrefix = CodeNormalization.ScoreDvdIdForCanonical("9OFJE-600");

        Assert.True(plain > withDigitPrefix);
    }

    [Fact]
    public void ScoreDvdIdForCanonical_PrefersPlainOverDistributorSuffix()
    {
        var plain = CodeNormalization.ScoreDvdIdForCanonical("OFJE-600");
        var withSuffix = CodeNormalization.ScoreDvdIdForCanonical("OFJE-600DOD");

        Assert.True(plain > withSuffix);
    }

    [Fact]
    public void ScoreDvdIdForCanonical_PosterAndTitle_IncreaseScore()
    {
        var bare = CodeNormalization.ScoreDvdIdForCanonical("OFJE-600");
        var withPosterAndTitle = CodeNormalization.ScoreDvdIdForCanonical("OFJE-600", hasPoster: true, hasTitle: true);

        Assert.True(withPosterAndTitle > bare);
    }

    [Fact]
    public void ScoreDvdIdForCanonical_BlankInput_ReturnsZero()
    {
        Assert.Equal(0, CodeNormalization.ScoreDvdIdForCanonical(""));
    }

    [Theory]
    [InlineData("9OFJE-600", true)]
    [InlineData("h_068OFJE-600", true)]
    [InlineData("h068OFJE600", true)]
    [InlineData("TKMIH-007", true)]
    [InlineData("OFJE-600DOD", true)]
    [InlineData("OFJE-600BOD", true)]
    [InlineData("OFJE-600", false)]
    [InlineData("SIVR-505", false)]
    public void IsDistributorRelease_DetectsKnownDistributorPatterns(string code, bool expected)
    {
        Assert.Equal(expected, CodeNormalization.IsDistributorRelease(code));
    }

    [Fact]
    public void IsDistributorRelease_BlankInput_ReturnsFalse()
    {
        Assert.False(CodeNormalization.IsDistributorRelease(null));
        Assert.False(CodeNormalization.IsDistributorRelease(""));
    }

    [Fact]
    public void GetZeroPaddingToleranceKey_SameCodeDifferentPadding_MatchesEachOther()
    {
        Assert.Equal(
            CodeNormalization.GetZeroPaddingToleranceKey("SAVR-1195"),
            CodeNormalization.GetZeroPaddingToleranceKey("SAVR-01195"));
    }

    [Theory]
    [InlineData("E2E-IMPORT-1")]
    [InlineData("E2E-001")]
    public void GetZeroPaddingToleranceKey_CodeWithDigitsInsideTheLetterPrefix_ReturnsNull(string code)
    {
        // Neither is a plain letters-then-digits code, so unlike CodeNormalization.GetCanonicalKey
        // (which strips both down to the same distributor-stripped key), this narrower key must
        // not treat them as comparable at all.
        Assert.Null(CodeNormalization.GetZeroPaddingToleranceKey(code));
    }

    [Fact]
    public void GetZeroPaddingToleranceKey_BlankInput_ReturnsNull()
    {
        Assert.Null(CodeNormalization.GetZeroPaddingToleranceKey(null));
        Assert.Null(CodeNormalization.GetZeroPaddingToleranceKey(""));
    }
}
