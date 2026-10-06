using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Torrents;

namespace Javbuddy.Tests.Services.Torrents;

public class SortMetadataQualityTests
{
    [Fact]
    public void Check_EmptyMovie_FlagsEverything()
    {
        var issues = SortMetadataQuality.Check(new MovieViewDto { Id = "ABC-123" });

        Assert.Equal([SortQualityIssue.MissingReleaseDate, SortQualityIssue.MissingActresses, SortQualityIssue.MissingMaker, SortQualityIssue.FallbackTitle], issues);
    }

    [Theory]
    [InlineData("ABC-123")]
    [InlineData("abc-123")]
    [InlineData("  ")]
    public void Check_TitleEqualToCodeOrBlank_IsFallback(string title) =>
        Assert.Contains(SortQualityIssue.FallbackTitle, SortMetadataQuality.Check(new MovieViewDto { Id = "ABC-123", Title = title }));

    [Fact]
    public void Check_CompleteMovie_HasNoIssues_AndYearAloneCountsAsDate()
    {
        var movie = new MovieViewDto { Id = "ABC-123", Title = "Real title", ReleaseYear = 2020, Maker = "S1", Actresses = [new() { FirstName = "A" }] };

        Assert.Empty(SortMetadataQuality.Check(movie));
    }

    [Fact]
    public void Describe_GivesTextForEveryIssue()
    {
        foreach (var issue in Enum.GetValues<SortQualityIssue>())
        {
            Assert.NotEqual(issue.ToString(), SortMetadataQuality.Describe(issue));
        }
    }
}
