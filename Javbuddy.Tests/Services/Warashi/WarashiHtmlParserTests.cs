using Javbuddy.Services.Warashi;

namespace Javbuddy.Tests.Services.Warashi;

public class WarashiHtmlParserTests
{
    private const string SampleSearchHtml = """
        <div id="bloc-nb-resultats" class="cadre">2 results</div>
        <div class="bloc-resultats cadre">
            <div class="resultat-pornostar correspondance_exacte">
                <a href="/en/s-2-0/yua-mikami/asian-female-pornstar/2922">
                    <img src="/WAPdB-img/pornostars-f/y/u/2922/preview/mini.jpg" alt="Yua MIKAMI" />
                </a>
                <p><a href="/en/s-2-0/yua-mikami/asian-female-pornstar/2922">
                    <span class="correspondance-lien">Yua MIKAMI</span> - 三上悠亜
                </a></p>
                <p>japanese pornstar / AV actress</p>
                <p>Porn/AV activity: 2015 - still active</p>
            </div>
            <div class="separateur-resultats"></div>
            <div class="resultat-pornostar">
                <a href="/en/s-4-1/mikami-wakana/female-pornstar/56807">
                    <img src="/WAPdB-img/par-defaut/pornostar-f-preview.jpg" alt="Mikami WAKANA" />
                </a>
                <p><a href="/en/s-4-1/mikami-wakana/female-pornstar/56807">
                    <span class="correspondance-lien">Mikami</span> WAKANA - 三上若菜
                </a></p>
                <p>japanese pornstar / AV actress</p>
                <p>Porn/AV activity: 2021 - retired</p>
                <p>AKA: Wakana - <span class="no-transform">わかな</span>, Chie - <span class="no-transform">ちえ</span></p>
            </div>
        </div>
        """;

    private const string SampleDetailHtml = """
        <div id="main">
        <div class="cadre" id="pornostar-profil" itemscope itemtype="http://schema.org/Person">
        <h1><span itemprop="name">Yua MIKAMI</span> - <span itemprop="additionalName">三上悠亜</span></h1>
        <meta itemprop="givenName" content="Yua" />
        <meta itemprop="familyName" content="MIKAMI" />
        <div id="pornostar-profil-photos">
            <div id="pornostar-profil-photos-0">
                <figure>
                    <img itemprop="image" alt="Yua MIKAMI" src="/WAPdB-img/pornostars-f/y/u/2922/large.jpg" />
                </figure>
            </div>
        </div>
        <div id="pornostar-profil-infos">
            <div class="pornostar-profil-carriere">
                <p>japanese pornstar / AV actress</p>
                <p>porn/AV activity: 2015 - still active</p>
            </div>
            <p>birthdate: <time itemprop="birthDate" content="1993-08-16">August 16, 1993</time></p>
            <p>birthplace: <span itemprop="addressCountry">Japan</span>, <span itemprop="addressRegion">Aichi prefecture</span>, <span itemprop="addressLocality">Nagoya</span></p>
            <p>measurements: JP 83-57-88 (US 33-22-35)</p>
            <p>cup size: F (= DDD)</p>
            <p itemprop="height" itemscope itemtype="http://schema.org/QuantitativeValue">height: <span itemprop="value">159</span> cm</p>
            <p>blood type: A</p>
            <div id="pornostar-profil-noms-alternatifs"><p>also known as:</p>
                <ul>
                    <li><span itemprop="additionalName">Momona KITO</span> - <span itemprop="additionalName">鬼頭桃菜</span></li>
                </ul>
            </div>
        </div>
        </div>
        </div>
        """;

    [Fact]
    public void ParseSearchResults_ExtractsExactAndOrdinaryMatches()
    {
        var results = WarashiHtmlParser.ParseSearchResults(SampleSearchHtml);

        Assert.Equal(2, results.Count);

        var first = results[0];
        Assert.Equal("Yua MIKAMI", first.Name);
        Assert.Equal("三上悠亜", first.JapaneseName);
        Assert.Equal("/en/s-2-0/yua-mikami/asian-female-pornstar/2922", first.PathOrUrl);
        Assert.True(first.IsExactMatch);
        Assert.Equal("2015 - still active", first.CareerActivity);
        Assert.Contains("mini.jpg", first.ImageUrl);

        var second = results[1];
        Assert.Equal("Mikami WAKANA", second.Name);
        Assert.Equal("三上若菜", second.JapaneseName);
        Assert.False(second.IsExactMatch);
        Assert.Contains("Wakana", second.KnownAliases);
        Assert.Contains("Chie", second.KnownAliases);
    }

    [Fact]
    public void ParsePerformerDetail_ExtractsAllBiographicalAndPhysicalAttributes()
    {
        var detail = WarashiHtmlParser.ParsePerformerDetail(SampleDetailHtml, "/en/s-2-0/yua-mikami/asian-female-pornstar/2922");

        Assert.NotNull(detail);
        Assert.Equal("Yua MIKAMI", detail.Name);
        Assert.Equal("Yua", detail.GivenName);
        Assert.Equal("MIKAMI", detail.FamilyName);
        Assert.Equal("三上悠亜", detail.JapaneseName);
        Assert.Equal(159, detail.HeightCm);
        Assert.Equal("F", detail.CupSize);
        Assert.Equal(83, detail.Bust);
        Assert.Equal(57, detail.Waist);
        Assert.Equal(88, detail.Hips);
        Assert.Equal(new DateTime(1993, 8, 16), detail.BirthDate);
        Assert.Equal("A", detail.BloodType);
        Assert.Equal("Japan, Aichi prefecture, Nagoya", detail.BirthPlace);
        Assert.False(detail.IsRetired);
        Assert.Equal(2015, detail.DebutYear);
        Assert.Contains("Momona KITO", detail.Aliases);
        Assert.Contains("鬼頭桃菜", detail.Aliases);
        Assert.NotNull(detail.MainPhotoUrl);
        Assert.Contains("large.jpg", detail.MainPhotoUrl);
    }

    [Fact]
    public void ParsePerformerDetail_NonStandardCupSize_ParsesAsNull()
    {
        var html = SampleDetailHtml.Replace("cup size: F (= DDD)", "cup size: L");

        var detail = WarashiHtmlParser.ParsePerformerDetail(html, "/en/s-2-0/yua-mikami/asian-female-pornstar/2922");

        Assert.NotNull(detail);
        Assert.Null(detail.CupSize);
        Assert.Equal(83, detail.Bust);
    }

    [Fact]
    public void ParsePerformerDetail_WhenAttributesAreUnknown_ParsesAsNullAndHasKnownAttributesIsFalse()
    {
        var html = """
            <div class="cadre" id="casting-profil" itemprop="about" itemscope itemtype="http://schema.org/Person">
                <meta itemprop="name" content="Haru ITÔ" />
                <meta itemprop="additionalName" content="伊藤はる" />
                <div id="casting-profil-mini-infos-details">
                    <p>birthdate: unknown</p>
                    <p>birthplace: unknown</p>
                    <p>astrological sign: unknown</p>
                    <p>measurements: unknown</p>
                    <p>cup size: unknown</p>
                    <p>height: unknown</p>
                    <p>blood type: unknown</p>
                </div>
            </div>
            """;

        var detail = WarashiHtmlParser.ParsePerformerDetail(html, "/en/s-4-1/haru-ito/female-pornstar/56136");

        Assert.NotNull(detail);
        Assert.Null(detail.CupSize);
        Assert.Null(detail.HeightCm);
        Assert.Null(detail.Bust);
        Assert.Null(detail.Waist);
        Assert.Null(detail.Hips);
        Assert.Null(detail.BirthDate);
        Assert.Null(detail.BloodType);
        Assert.Null(detail.BirthPlace);
        Assert.Null(detail.IsRetired);
        Assert.False(detail.HasKnownAttributes);
    }
}
