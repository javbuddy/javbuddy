using System.Text.Json;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Torrents;

namespace Javbuddy.Tests.Services.Torrents;

public class SortSourceHelperTests
{
    [Fact]
    public void ParseSources_ReadsStringMap_IgnoresNonStrings()
    {
        var json = JsonDocument.Parse("""{"title":"r18dev","maker":"dmm","weird":3}""").RootElement;

        var map = SortSourceHelper.ParseSources(json);

        Assert.Equal(2, map.Count);
        Assert.Equal("dmm", map["MAKER"]);
        Assert.Empty(SortSourceHelper.ParseSources(null));
    }

    [Fact]
    public void CopyField_CopiesOnlyThatField()
    {
        var from = new MovieViewDto { Maker = "S1", Title = "From", ReleaseDate = new DateTime(2020, 1, 2), ReleaseYear = 2020, Genres = [new() { Name = "VR" }] };
        var to = new MovieViewDto { Maker = "Old", Title = "Mine", Genres = [] };

        SortSourceHelper.CopyField("maker", from, to);
        SortSourceHelper.CopyField("release_date", from, to);
        SortSourceHelper.CopyField("genres", from, to);

        Assert.Equal(("S1", "Mine", (int?)2020), (to.Maker, to.Title, to.ReleaseYear));
        Assert.Equal("VR", Assert.Single(to.Genres!).Name);
        Assert.NotSame(from.Genres, to.Genres);
    }

    [Fact]
    public void CopyField_EveryOverridableFieldIsHandled()
    {
        var from = new MovieViewDto
        {
            Title = "t",
            OriginalTitle = "o",
            Description = "d",
            Director = "di",
            Maker = "m",
            Label = "l",
            Series = "s",
            Runtime = 9,
            ReleaseDate = new DateTime(2020, 1, 2),
            ReleaseYear = 2020,
            Actresses = [new() { FirstName = "A" }],
            Genres = [new() { Name = "G" }]
        };
        var to = new MovieViewDto();

        foreach (var field in SortSourceHelper.OverridableFields)
        {
            SortSourceHelper.CopyField(field, from, to);
        }

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(from), System.Text.Json.JsonSerializer.Serialize(to));
    }

    [Fact]
    public void FormatValue_RendersListsAndDates()
    {
        var s = new ScraperSourceResultDto { Source = "dmm", ReleaseDate = new DateTime(2020, 1, 2), Runtime = 0, Genres = ["VR", "Solo"], Actresses = [new() { FirstName = "Yua", LastName = "Mikami" }] };

        Assert.Equal("2020-01-02", SortSourceHelper.FormatValue(s, "release_date"));
        Assert.Equal("VR, Solo", SortSourceHelper.FormatValue(s, "genres"));
        Assert.Equal("Yua Mikami", SortSourceHelper.FormatValue(s, "actresses"));
        Assert.Null(SortSourceHelper.FormatValue(s, "maker"));
        Assert.Null(SortSourceHelper.FormatValue(s, "runtime"));
    }

    [Fact]
    public void NormalizeActressNameKey_MatchesJavinizerGo()
    {
        Assert.Equal("榊原 萌", SortSourceHelper.NormalizeActressNameKey("榊原\u3000萌"));
        Assert.Equal("yua mikami", SortSourceHelper.NormalizeActressNameKey("  Yua   MIKAMI "));
        Assert.Equal("abc", SortSourceHelper.NormalizeActressNameKey("\uFF21\uFF22\uFF23")); // full-width
        Assert.Equal("", SortSourceHelper.NormalizeActressNameKey(" "));
    }

    [Fact]
    public void ActressSource_PrefersDmmIdThenJapaneseThenEitherNameOrder()
    {
        var sources = new Dictionary<string, string>
        {
            ["dmmid:7"] = "dmm",
            ["name:新木 希空"] = "r18dev",
            ["name:mikami yua"] = "javlibrary"
        };

        Assert.Equal("dmm", SortSourceHelper.ActressSource(sources, new ActressViewDto { DmmId = 7, JapaneseName = "新木 希空" }));
        Assert.Equal("r18dev", SortSourceHelper.ActressSource(sources, new ActressViewDto { DmmId = 8, JapaneseName = "新木　希空" }));
        Assert.Equal("javlibrary", SortSourceHelper.ActressSource(sources, new ActressViewDto { FirstName = "Yua", LastName = "Mikami" }));
        Assert.Null(SortSourceHelper.ActressSource(sources, new ActressViewDto { FirstName = "Nobody" }));
        Assert.Null(SortSourceHelper.ActressSource(new Dictionary<string, string>(), new ActressViewDto { DmmId = 7 }));
    }
}
