using Javbuddy.Services.MinnanoAv;

namespace Javbuddy.Tests.Services.MinnanoAv;

public class MinnanoAvHtmlParserTests
{
    private const string SampleSearchHtml = """
        <table width="100%" cellspacing="0" cellpadding="0" border="0" class="tbllist actress">
        <tr>
            <th class="t9"></th>
            <th class="t9" align="left">名前</th>
        </tr>
        <tr>
            <td><a href="actress227711.html?北野美帆子(人妻斬り)"><img src="p_actress_125_125/023/227711.jpg" alt="北野美帆子(人妻斬り)" width="100" height="100" loading="lazy" /></a></td>
            <td class="details">
                <h2 class="ttl"><a href="actress227711.html?北野美帆子(人妻斬り)">北野美帆子(人妻斬り)</a></h2>
                <p class="furi">きたのみほこ / Kitano Mihoko</p>
                <p class="debut-info">
                    2023年06月デビュー
                </p>
            </td>
            <td>2</td>
        </tr>
        <tr>
            <td><a href="actress263514.html?美帆(E-BODY)"><img src="p_actress_125_125/018/263514.jpg" alt="美帆(E-BODY)" width="100" height="100" loading="lazy" /></a></td>
            <td class="details">
                <h2 class="ttl"><a href="actress263514.html?美帆(E-BODY)">美帆(E-BODY)</a></h2>
                <p class="furi">みほ / Miho</p>
                <p class="furi">（倉吉美帆）</p>
                <p class="debut-info">
                    2019年01月デビュー
                </p>
            </td>
            <td>10</td>
        </tr>
        </table>
        """;

    private const string SampleDetailHtml = """
        <div class="act-profile">
        <table width="100%" cellspacing="0" cellpadding="0" border="0">
            <tr>
                <td><h2>通野未帆 （とおのみほ / Tohno Miho）</h2></td>
            </tr>
            <tr>
                <td><span>別名</span><p>澤口美帆 （さわぐちみほ / sawaguchi miho）</p></td>
            </tr>
            <tr>
                <td><span>別名</span><p>有村ちはる （ありむらちはる / Arimura Chiharu）</p></td>
            </tr>
            <tr>
                <td><span>生年月日</span><p>1991年01月21日
                    （現在 <a href="actress_list.php?birthday=1991-01-21">35歳</a>）みずがめ座</td>		</p></td>
            </tr>
            <tr>
                <td><span>サイズ</span><p>T160 / B84(<a href="actress_list.php?cup=E">Eカップ</a>) / W59 / H85 / S23.5</p></td>
            </tr>
            <tr>
                <td><span>血液型</span><p><a href="actress_list.php?blood_type=A">A型</a></p></td>
            </tr>
            <tr valign="top">
                <td><span>タグ</span>
                    <div class="tagarea">
                        <a href="actress_list.php?tag_a_id=61">美人</a>
                        <a href="actress_list.php?tag_a_id=28">美乳</a>
                    </div>
                </td>
            </tr>
        </table>
        </div>
        <section class="side-tag side-column">
        <h2><i class="label"></i>AV女優 人気のタグ</h2>
        <ul>
            <li><a href="/actress_list.php?tag_a_id=124">名女優</a></li>
            <li><a href="/actress_list.php?tag_a_id=124">引退</a></li>
        </ul>
        </section>
        """;

    private const string RetiredDetailHtml = """
        <div class="act-profile">
        <table>
            <tr><td><h2>麻美ゆま （あさみゆま / Yuma Asami）</h2></td></tr>
            <tr valign="top">
                <td><span>タグ</span>
                    <div class="tagarea">
                        <a href="actress_list.php?tag_a_id=123">引退</a>
                        <a href="actress_list.php?tag_a_id=61">美人</a>
                    </div>
                </td>
            </tr>
        </table>
        </div>
        """;

    [Fact]
    public void ParseSearchResults_ExtractsBothCandidateRows()
    {
        var results = MinnanoAvHtmlParser.ParseSearchResults(SampleSearchHtml);

        Assert.Equal(2, results.Count);

        var first = results[0];
        Assert.Equal("北野美帆子(人妻斬り)", first.Name);
        Assert.Equal("きたのみほこ", first.Kana);
        Assert.Equal("Kitano Mihoko", first.Romaji);
        Assert.Equal("actress227711.html", first.PathOrUrl);
        Assert.False(first.IsExactMatch);
        Assert.Contains("227711.jpg", first.ImageUrl);
        Assert.Empty(first.KnownAliases);

        var second = results[1];
        Assert.Equal("美帆(E-BODY)", second.Name);
        Assert.Equal("みほ", second.Kana);
        Assert.Equal("Miho", second.Romaji);
        Assert.Equal("actress263514.html", second.PathOrUrl);
        Assert.Contains("倉吉美帆", second.KnownAliases);
    }

    [Fact]
    public void ParsePerformerDetail_ExtractsAllBiographicalAndPhysicalAttributes()
    {
        var detail = MinnanoAvHtmlParser.ParsePerformerDetail(SampleDetailHtml, "actress148426.html");

        Assert.NotNull(detail);
        Assert.Equal("通野未帆", detail.Name);
        Assert.Equal("とおのみほ", detail.Kana);
        Assert.Equal("Tohno Miho", detail.Romaji);
        Assert.Equal(160, detail.HeightCm);
        Assert.Equal("E", detail.CupSize);
        Assert.Equal(84, detail.Bust);
        Assert.Equal(59, detail.Waist);
        Assert.Equal(85, detail.Hips);
        Assert.Equal(new DateTime(1991, 1, 21), detail.BirthDate);
        Assert.False(detail.IsRetired);
        Assert.Contains("澤口美帆", detail.Aliases);
        Assert.Contains("有村ちはる", detail.Aliases);
        Assert.True(detail.HasKnownAttributes);
    }

    [Fact]
    public void ParsePerformerDetail_WhenSitewidePopularTagsSidebarListsRetired_DoesNotFalsePositive()
    {
        // Regression test: "AV女優 人気のタグ" is a site-wide "popular tags" widget rendered
        // identically on every actress page (with a mislabeled tag_a_id, per the live site) — it
        // must never be read as this actress's own retirement status. SampleDetailHtml's own
        // <span>タグ</span> tagarea deliberately omits 引退 while the sidebar below it includes it.
        var detail = MinnanoAvHtmlParser.ParsePerformerDetail(SampleDetailHtml, "actress148426.html");

        Assert.NotNull(detail);
        Assert.False(detail.IsRetired);
    }

    [Fact]
    public void ParsePerformerDetail_NonStandardCupSize_ParsesAsNull()
    {
        var html = SampleDetailHtml.Replace("cup=E\">Eカップ", "cup=L\">Lカップ");

        var detail = MinnanoAvHtmlParser.ParsePerformerDetail(html, "actress148426.html");

        Assert.NotNull(detail);
        Assert.Null(detail.CupSize);
        Assert.Equal(84, detail.Bust);
    }

    [Fact]
    public void ParsePerformerDetail_WhenOwnTagareaHasRetiredTag_IsRetiredIsTrue()
    {
        var detail = MinnanoAvHtmlParser.ParsePerformerDetail(RetiredDetailHtml, "actress294802.html");

        Assert.NotNull(detail);
        Assert.True(detail.IsRetired);
    }

    [Fact]
    public void ParsePerformerDetail_WhenNoTagsSectionPresent_IsRetiredIsFalse()
    {
        const string html = """
            <div class="act-profile">
            <table>
                <tr><td><h2>美帆 （みほ / Miho）</h2></td></tr>
                <tr><td><span>サイズ</span><p>T160 / B84(<a href="x">Eカップ</a>) / W59 / H85</p></td></tr>
            </table>
            </div>
            """;

        var detail = MinnanoAvHtmlParser.ParsePerformerDetail(html, "actress1.html");

        Assert.NotNull(detail);
        Assert.False(detail.IsRetired);
    }

    [Fact]
    public void ParsePerformerDetail_WhenHeaderMissing_ReturnsNull()
    {
        var detail = MinnanoAvHtmlParser.ParsePerformerDetail("<html><body>no profile here</body></html>", "actress1.html");

        Assert.Null(detail);
    }

    [Fact]
    public void ParseSearchResults_WhenNoRowsPresent_ReturnsEmpty()
    {
        var results = MinnanoAvHtmlParser.ParseSearchResults("""<table class="tbllist actress"></table>""");

        Assert.Empty(results);
    }

    [Fact]
    public void ParseOgImage_ExtractsThumbnailUrl()
    {
        const string html = """<meta property="og:image" content="https://www.minnano-av.com/p_actress_125_125/006/148426.jpg?new">""";

        var url = MinnanoAvHtmlParser.ParseOgImage(html);

        Assert.Equal("https://www.minnano-av.com/p_actress_125_125/006/148426.jpg?new", url);
    }
}
