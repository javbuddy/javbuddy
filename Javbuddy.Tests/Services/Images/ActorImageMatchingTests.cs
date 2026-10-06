using Javbuddy.Services.Images;

namespace Javbuddy.Tests.Services.Images;

public class ActorImageMatchingTests
{
    [Theory]
    [InlineData("Hashimoto_Arina", "hashimotoarina")]
    [InlineData("Arina-Hashimoto", "arinahashimoto")]
    [InlineData("Hashimoto.Arina", "hashimotoarina")]
    [InlineData("Hashimoto Arina", "hashimotoarina")]
    [InlineData("  Hashimoto   Arina  ", "hashimotoarina")]
    [InlineData("橋本 ありな", "橋本ありな")]
    [InlineData("橋本_ありな", "橋本ありな")]
    [InlineData("波多野 結衣", "波多野結衣")]
    [InlineData("Tsubomi", "tsubomi")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeForMatching_NormalizesCorrectly(string? input, string expected)
    {
        var result = ActorImageMatching.NormalizeForMatching(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GenerateCandidateKeys_SingleToken_GeneratesExact()
    {
        var keys = ActorImageMatching.GenerateCandidateKeys(["Tsubomi"]);
        Assert.Contains("Tsubomi", keys);
    }

    [Fact]
    public void GenerateCandidateKeys_TwoTokens_GeneratesSeparatorsAndReversed()
    {
        var keys = ActorImageMatching.GenerateCandidateKeys(["Hashimoto Arina"]);

        // Original order variants
        Assert.Contains("Hashimoto Arina", keys);
        Assert.Contains("Hashimoto_Arina", keys);
        Assert.Contains("Hashimoto-Arina", keys);
        Assert.Contains("Hashimoto.Arina", keys);
        Assert.Contains("HashimotoArina", keys);

        // Reversed order variants
        Assert.Contains("Arina Hashimoto", keys);
        Assert.Contains("Arina_Hashimoto", keys);
        Assert.Contains("Arina-Hashimoto", keys);
        Assert.Contains("Arina.Hashimoto", keys);
        Assert.Contains("ArinaHashimoto", keys);
    }

    [Fact]
    public void GenerateCandidateKeys_JapaneseSpaced_GeneratesCompact()
    {
        var keys = ActorImageMatching.GenerateCandidateKeys(["橋本 ありな"]);
        Assert.Contains("橋本 ありな", keys);
        Assert.Contains("橋本ありな", keys);
    }

    [Fact]
    public void FindBestMatch_ExactCandidateMatch_WinsOverVariants()
    {
        var files = new[]
        {
            "/movies/SSNI-081/.actors/Arina_Hashimoto.jpg",
            "/movies/SSNI-081/.actors/Hashimoto_Arina.jpg"
        };

        var match = ActorImageMatching.FindBestMatch(files, ["Hashimoto Arina"]);
        Assert.Equal("/movies/SSNI-081/.actors/Hashimoto_Arina.jpg", match);
    }

    [Fact]
    public void FindBestMatch_MatchesHyphensAndDots()
    {
        var files = new[]
        {
            "/movies/MIDE-123/.actors/Mikami-Yua.jpg"
        };

        var match = ActorImageMatching.FindBestMatch(files, ["Mikami Yua"]);
        Assert.Equal("/movies/MIDE-123/.actors/Mikami-Yua.jpg", match);

        var dottedFiles = new[]
        {
            "/movies/MIDE-123/.actors/Mikami.Yua.png"
        };

        var dotMatch = ActorImageMatching.FindBestMatch(dottedFiles, ["Mikami Yua"]);
        Assert.Equal("/movies/MIDE-123/.actors/Mikami.Yua.png", dotMatch);
    }

    [Fact]
    public void FindBestMatch_MatchesSwappedOrder()
    {
        var files = new[]
        {
            "/movies/SSNI-081/.actors/Arina_Hashimoto.jpg"
        };

        // Caller only provides "Hashimoto Arina"
        var match = ActorImageMatching.FindBestMatch(files, ["Hashimoto Arina"]);
        Assert.Equal("/movies/SSNI-081/.actors/Arina_Hashimoto.jpg", match);
    }

    [Fact]
    public void FindBestMatch_MatchesCompactName()
    {
        var files = new[]
        {
            "/movies/MIDE-123/.actors/MikamiYua.webp"
        };

        var match = ActorImageMatching.FindBestMatch(files, ["Mikami Yua"]);
        Assert.Equal("/movies/MIDE-123/.actors/MikamiYua.webp", match);
    }

    [Fact]
    public void FindBestMatch_MatchesJapaneseCompactAndSpaced()
    {
        var files1 = new[] { "/movies/.actors/波多野結衣.jpg" };
        var match1 = ActorImageMatching.FindBestMatch(files1, ["波多野 結衣"]);
        Assert.Equal("/movies/.actors/波多野結衣.jpg", match1);

        var files2 = new[] { "/movies/.actors/波多野 結衣.jpg" };
        var match2 = ActorImageMatching.FindBestMatch(files2, ["波多野結衣"]);
        Assert.Equal("/movies/.actors/波多野 結衣.jpg", match2);
    }

    [Fact]
    public void FindBestMatch_MatchesSubdirectoryNamedAfterActor()
    {
        var files = new[]
        {
            "/movies/SSNI-081/.actors/Hashimoto Arina/headshot.jpg"
        };

        var match = ActorImageMatching.FindBestMatch(files, ["Hashimoto Arina"]);
        Assert.Equal("/movies/SSNI-081/.actors/Hashimoto Arina/headshot.jpg", match);
    }

    [Fact]
    public void FindBestMatch_IgnoresNonImageFiles()
    {
        var files = new[]
        {
            "/movies/SSNI-081/.actors/Thumbs.db",
            "/movies/SSNI-081/.actors/Hashimoto Arina.txt",
            "/movies/SSNI-081/.actors/Hashimoto Arina.nfo"
        };

        var match = ActorImageMatching.FindBestMatch(files, ["Hashimoto Arina"]);
        Assert.Null(match);
    }

    [Fact]
    public void FindBestMatch_SupportsJfifAndCaseInsensitiveExtensions()
    {
        var files = new[]
        {
            "/movies/SSNI-081/.actors/Hashimoto_Arina.JFIF",
            "/movies/SSNI-081/.actors/Tsubomi.JPG"
        };

        var match1 = ActorImageMatching.FindBestMatch(files, ["Hashimoto Arina"]);
        Assert.Equal("/movies/SSNI-081/.actors/Hashimoto_Arina.JFIF", match1);

        var match2 = ActorImageMatching.FindBestMatch(files, ["Tsubomi"]);
        Assert.Equal("/movies/SSNI-081/.actors/Tsubomi.JPG", match2);
    }
}
