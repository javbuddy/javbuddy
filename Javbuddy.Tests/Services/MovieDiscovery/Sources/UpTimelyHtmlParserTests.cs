using Javbuddy.Services.MovieDiscovery.Sources;

namespace Javbuddy.Tests.Services.MovieDiscovery.Sources;

public class UpTimelyHtmlParserTests
{
    public static IEnumerable<object[]> SiteKeys => UpTimelySiteFixtures.All.Keys.Select(key => new object[] { key });

    [Theory]
    [MemberData(nameof(SiteKeys))]
    public void ParseReleaseListing_AssignsNearestPrecedingDateHeaderToEachCard(string site)
    {
        var fixture = UpTimelySiteFixtures.All[site];
        var parser = new UpTimelyHtmlParser(fixture.BaseUrl, fixture.Studio);

        var items = parser.ParseReleaseListing(fixture.SampleReleaseHtml);

        Assert.Equal(3, items.Count);

        var first = items[0];
        Assert.Equal(fixture.FirstCode, first.Code);
        Assert.Equal(fixture.FirstTitle, first.Title);
        Assert.Equal(fixture.FirstCover, first.CoverImageUrl);
        Assert.Equal(fixture.FirstDate, first.ReleaseDate);
        Assert.False(first.IsUpcoming);
        Assert.Equal(fixture.Studio, first.Studio);

        Assert.Equal(fixture.SecondCode, items[1].Code);
        Assert.Equal(fixture.SecondDate, items[1].ReleaseDate);

        Assert.Equal(fixture.ThirdCode, items[2].Code);
        Assert.Equal(fixture.SecondDate, items[2].ReleaseDate);
    }

    [Theory]
    [MemberData(nameof(SiteKeys))]
    public void ParseReserveListing_ReturnsCandidatesWithNoExactReleaseDateAndUpcomingTrue(string site)
    {
        var fixture = UpTimelySiteFixtures.All[site];
        var parser = new UpTimelyHtmlParser(fixture.BaseUrl, fixture.Studio);

        var items = parser.ParseReserveListing(fixture.SampleReserveHtml);

        var item = Assert.Single(items);
        Assert.Equal(fixture.ReserveCode, item.Code);
        Assert.Equal(fixture.ReserveTitle, item.Title);
        Assert.Null(item.ReleaseDate);
        Assert.True(item.IsUpcoming);
    }

    [Theory]
    [MemberData(nameof(SiteKeys))]
    public void ParseGalleryImageUrls_ReturnsOnlyDistinctDetailGalleryImagesInOrder(string site)
    {
        var fixture = UpTimelySiteFixtures.All[site];
        var parser = new UpTimelyHtmlParser(fixture.BaseUrl, fixture.Studio);

        var imageUrls = parser.ParseGalleryImageUrls(fixture.SampleDetailHtml);

        Assert.Equal([fixture.GalleryImage1, fixture.GalleryImage2], imageUrls);
    }

    [Theory]
    [MemberData(nameof(SiteKeys))]
    public void ParseActressNames_ReturnsNamesFromTheActressFieldOnly(string site)
    {
        var fixture = UpTimelySiteFixtures.All[site];
        var parser = new UpTimelyHtmlParser(fixture.BaseUrl, fixture.Studio);

        var names = parser.ParseActressNames(fixture.SampleDetailHtml);

        Assert.Equal([fixture.Actress1, fixture.Actress2], names);
    }

    [Theory]
    [MemberData(nameof(SiteKeys))]
    public void ParseActressNames_NoActressField_ReturnsEmpty(string site)
    {
        var fixture = UpTimelySiteFixtures.All[site];
        var parser = new UpTimelyHtmlParser(fixture.BaseUrl, fixture.Studio);

        var names = parser.ParseActressNames("<div>no actress field here</div>");

        Assert.Empty(names);
    }

    [Fact]
    public void ParseActressNames_ExcludesMaleActors_WhenBothActressAndActorFieldsPresent()
    {
        var parser = new UpTimelyHtmlParser("https://s1s1s1.com", "S1 NO.1 STYLE");
        var html = """
            <div class="item">
                <div class="th">女優</div>
                <div class="td">
                    <div class="item">
                        <a class="c-tag" href="https://s1s1s1.com/actress/detail/870592">白石透羽</a>
                    </div>
                </div>
            </div>
            <div class="item">
                <div class="th">男優</div>
                <div class="td">
                    <div class="item">
                        <a class="c-tag" href="https://s1s1s1.com/actress/detail/99999">しみけん</a>
                    </div>
                </div>
            </div>
            <div class="item">
                <div class="th">発売日</div>
                <div class="td">2026年9月14日</div>
            </div>
            """;

        var names = parser.ParseActressNames(html);

        Assert.Equal(["白石透羽"], names);
    }

    [Fact]
    public void ParseActressNames_ExcludesMaleActors_WhenActorFieldHasNoItemWrapper()
    {
        var parser = new UpTimelyHtmlParser("https://s1s1s1.com", "S1 NO.1 STYLE");
        var html = """
            <div class="item">
                <div class="th">女優</div>
                <div class="td">
                    <div class="item">
                        <a class="c-tag" href="https://s1s1s1.com/actress/detail/870592">白石透羽</a>
                    </div>
                </div>
                <div class="th">男優</div>
                <div class="td">
                    <div class="item">
                        <a class="c-tag" href="https://s1s1s1.com/actress/detail/99999">しみけん</a>
                    </div>
                </div>
            </div>
            <div class="item">
                <div class="th">発売日</div>
                <div class="td">2026年9月14日</div>
            </div>
            """;

        var names = parser.ParseActressNames(html);

        Assert.Equal(["白石透羽"], names);
    }

    [Fact]
    public void ParseActressNames_ExcludesMaleActors_WhenActorPrecedesActress()
    {
        var parser = new UpTimelyHtmlParser("https://s1s1s1.com", "S1 NO.1 STYLE");
        var html = """
            <div class="item">
                <div class="th">男優</div>
                <div class="td">
                    <div class="item">
                        <a class="c-tag" href="https://s1s1s1.com/actress/detail/99999">しみけん</a>
                    </div>
                </div>
            </div>
            <div class="item">
                <div class="th">女優</div>
                <div class="td">
                    <div class="item">
                        <a class="c-tag" href="https://s1s1s1.com/actress/detail/870592">白石透羽</a>
                    </div>
                </div>
            </div>
            <div class="item">
                <div class="th">発売日</div>
                <div class="td">2026年9月14日</div>
            </div>
            """;

        var names = parser.ParseActressNames(html);

        Assert.Equal(["白石透羽"], names);
    }

    [Fact]
    public void ParseActressNames_OnlyMaleActorField_ReturnsEmpty()
    {
        var parser = new UpTimelyHtmlParser("https://s1s1s1.com", "S1 NO.1 STYLE");
        var html = """
            <div class="item">
                <div class="th">男優</div>
                <div class="td">
                    <div class="item">
                        <a class="c-tag" href="https://s1s1s1.com/actress/detail/99999">しみけん</a>
                    </div>
                </div>
            </div>
            <div class="item">
                <div class="th">発売日</div>
                <div class="td">2026年9月14日</div>
            </div>
            """;

        var names = parser.ParseActressNames(html);

        Assert.Empty(names);
    }

    [Fact]
    public void ParseActressNames_ActressFieldIsLastItemInTable_ReturnsNames()
    {
        var parser = new UpTimelyHtmlParser("https://s1s1s1.com", "S1 NO.1 STYLE");
        var html = """
            <div class="item">
                <div class="th">発売日</div>
                <div class="td">2026年9月14日</div>
            </div>
            <div class="item">
                <div class="th">女優</div>
                <div class="td">
                    <div class="item">
                        <a class="c-tag" href="https://s1s1s1.com/actress/detail/870592">白石透羽</a>
                    </div>
                </div>
            </div>
            """;

        var names = parser.ParseActressNames(html);

        Assert.Equal(["白石透羽"], names);
    }

    [Fact]
    public void ParseMaleActorNames_ReturnsMaleActorsFromActorField()
    {
        var parser = new UpTimelyHtmlParser("https://s1s1s1.com", "S1 NO.1 STYLE");
        var html = """
            <div class="item">
                <div class="th">女優</div>
                <div class="td">
                    <div class="item">
                        <a class="c-tag" href="https://s1s1s1.com/actress/detail/870592">白石透羽</a>
                    </div>
                </div>
            </div>
            <div class="item">
                <div class="th">男優</div>
                <div class="td">
                    <div class="item">
                        <a class="c-tag" href="https://s1s1s1.com/actor/detail/99999">しみけん</a>
                    </div>
                    <div class="item">
                        <a class="c-tag" href="https://s1s1s1.com/actress/detail/88888">森林原人</a>
                    </div>
                </div>
            </div>
            """;

        var maleActors = parser.ParseMaleActorNames(html);

        Assert.Equal(["しみけん", "森林原人"], maleActors);
    }

    [Theory]
    [MemberData(nameof(SiteKeys))]
    public void NormalizeCode_InsertsHyphenBetweenLettersAndTrailingDigits(string site)
    {
        var fixture = UpTimelySiteFixtures.All[site];

        Assert.Equal(fixture.ExpectedCode1, UpTimelyHtmlParser.NormalizeCode(fixture.RawCode1));
        Assert.Equal(fixture.ExpectedCode2, UpTimelyHtmlParser.NormalizeCode(fixture.RawCode2));
        Assert.Equal("already-hyphenated", UpTimelyHtmlParser.NormalizeCode("already-hyphenated"));
    }
}
