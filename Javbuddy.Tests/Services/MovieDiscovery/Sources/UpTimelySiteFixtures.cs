namespace Javbuddy.Tests.Services.MovieDiscovery.Sources;

/// <summary>Real captured markup (trimmed) for each studio site sharing the up-timely.com
/// template, one fixture per site, shared between <see cref="UpTimelyHtmlParserTests"/> and
/// <see cref="UpTimelyDiscoverySourceTests"/> so the same real-site data backs both.</summary>
public sealed record UpTimelySiteFixture
{
    public required string BaseUrl { get; init; }

    public required string SourceName { get; init; }

    public required string Studio { get; init; }

    public required string SampleReleaseHtml { get; init; }

    public required string SampleReserveHtml { get; init; }

    public required string SampleDetailHtml { get; init; }

    public required string FirstCode { get; init; }

    public required string FirstTitle { get; init; }

    public required string FirstCover { get; init; }

    public required DateTime FirstDate { get; init; }

    public required string SecondCode { get; init; }

    public required string ThirdCode { get; init; }

    public required DateTime SecondDate { get; init; }

    public required string ReserveCode { get; init; }

    public required string ReserveTitle { get; init; }

    public required string GalleryImage1 { get; init; }

    public required string GalleryImage2 { get; init; }

    public required string Actress1 { get; init; }

    public required string Actress2 { get; init; }

    public required string RawCode1 { get; init; }

    public required string ExpectedCode1 { get; init; }

    public required string RawCode2 { get; init; }

    public required string ExpectedCode2 { get; init; }

    public required string SingleCardReleaseListingHtml { get; init; }

    public required string SingleCardCode { get; init; }

    public required string SingleCardDetailHtml { get; init; }

    public required string SingleCardGalleryImage1 { get; init; }

    public required string SingleCardGalleryImage2 { get; init; }
}

public static class UpTimelySiteFixtures
{
    public static readonly IReadOnlyDictionary<string, UpTimelySiteFixture> All = new Dictionary<string, UpTimelySiteFixture>
    {
        ["S1"] = new()
        {
            BaseUrl = "https://s1s1s1.com",
            SourceName = "S1",
            Studio = "S1 NO.1 STYLE",
            // Trimmed from a real capture of https://s1s1s1.com/works/list/release (2026-09-15) —
            // two date sections, the second with two cards, to exercise "nearest preceding
            // header" assignment.
            SampleReleaseHtml = """
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap">
                    <p>2026年9月14日発売</p>
                    <p>1作品</p>
                </div>
                <div class="l-wrap">
                    <div class="c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://s1s1s1.com/works/detail/SIVR501">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/2/content/86354/cover1.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">VR NO.1 STYLE Sample Title One</p>
                                    </div>
                                </a>
                                <a class="name c-main-font-hover" href="https://s1s1s1.com/actress/detail/870592">Actress One</a>
                            </div>
                        </div>
                    </div>
                </div>
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap">
                    <p>2026年9月8日発売</p>
                    <p>2作品</p>
                </div>
                <div class="l-wrap">
                    <div class="c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://s1s1s1.com/works/detail/SNOS310">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/2/content/2/cover2.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Title Two</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://s1s1s1.com/works/detail/OFJE656">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/2/content/3/cover3.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Title Three</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
                """,
            // Trimmed from a real capture of https://s1s1s1.com/works/list/reserve (2026-09-15) —
            // cards sit in a swiper carousel wrapper instead of a plain grid, and there's no
            // per-item date.
            SampleReserveHtml = """
                <div class="p-search__key l-wrap">
                    <p>2026年9月21日〜10月13日発売</p>
                </div>
                <div class="swiper-wrapper">
                    <div class="swiper-slide c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://s1s1s1.com/works/detail/SNOS343">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/2/content/87549/cover4.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Upcoming Title</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
                """,
            SampleDetailHtml = """
                <div class="swiper-parent">
                    <div class="swiper-container">
                        <div class="swiper-wrapper">
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/2/content/87038/first.jpg" alt="">
                            </div>
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/2/content/87038/second.jpg" alt="">
                            </div>
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/2/content/87038/first.jpg" alt="">
                            </div>
                        </div>
                    </div>
                </div>
                <div class="c-card">
                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/2/content/other/related.jpg" alt="">
                </div>
                <div class="item">
                    <div class="th">女優</div>
                    <div class="td">
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://s1s1s1.com/actress/detail/870592">白石透羽</a>
                        </div>
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://s1s1s1.com/actress/detail/111111">第二女優</a>
                        </div>
                    </div>
                </div>
                <div class="item">
                    <div class="th">発売日</div>
                    <div class="td">
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://s1s1s1.com/works/list/date/2026-09-14">2026年9月14日</a>
                        </div>
                    </div>
                </div>
                """,
            FirstCode = "SIVR-501",
            FirstTitle = "VR NO.1 STYLE Sample Title One",
            FirstCover = "https://cdn.up-timely.com/image/2/content/86354/cover1.jpg",
            FirstDate = new DateTime(2026, 9, 14),
            SecondCode = "SNOS-310",
            ThirdCode = "OFJE-656",
            SecondDate = new DateTime(2026, 9, 8),
            ReserveCode = "SNOS-343",
            ReserveTitle = "Sample Upcoming Title",
            GalleryImage1 = "https://cdn.up-timely.com/image/2/content/87038/first.jpg",
            GalleryImage2 = "https://cdn.up-timely.com/image/2/content/87038/second.jpg",
            Actress1 = "白石透羽",
            Actress2 = "第二女優",
            RawCode1 = "SIVR501",
            ExpectedCode1 = "SIVR-501",
            RawCode2 = "OFJE656",
            ExpectedCode2 = "OFJE-656",
            SingleCardReleaseListingHtml = """
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap"><p>2026年9月14日発売</p></div>
                <div class="item"><div class="c-card">
                    <a class="img hover" href="https://s1s1s1.com/works/detail/SNOS310">
                        <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/2/content/87038/cover.jpg" alt=""/>
                        <div class="hover__child"><p class="text">Sample title</p></div>
                    </a>
                </div></div>
                """,
            SingleCardCode = "SNOS-310",
            SingleCardDetailHtml = """
                <div class="swiper-wrapper">
                    <div class="swiper-slide"><img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/2/content/87038/one.jpg" alt=""></div>
                    <div class="swiper-slide"><img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/2/content/87038/two.jpg" alt=""></div>
                </div>
                """,
            SingleCardGalleryImage1 = "https://cdn.up-timely.com/image/2/content/87038/one.jpg",
            SingleCardGalleryImage2 = "https://cdn.up-timely.com/image/2/content/87038/two.jpg",
        },
        ["Moodyz"] = new()
        {
            BaseUrl = "https://moodyz.com",
            SourceName = "Moodyz",
            Studio = "MOODYZ",
            // Trimmed from a real capture of https://moodyz.com/works/list/release (2026-09-16) —
            // two date sections, the second with two cards, to exercise "nearest preceding
            // header" assignment.
            SampleReleaseHtml = """
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap">
                    <p>2026年9月15日発売</p>
                    <p>1作品</p>
                </div>
                <div class="l-wrap">
                    <div class="c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://moodyz.com/works/detail/MIRD281">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/30/content/87274/cover1.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Title One</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap">
                    <p>2026年9月8日発売</p>
                    <p>2作品</p>
                </div>
                <div class="l-wrap">
                    <div class="c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://moodyz.com/works/detail/MIKR123">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/30/content/2/cover2.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Title Two</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://moodyz.com/works/detail/MIAB677">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/30/content/3/cover3.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Title Three</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
                """,
            // Trimmed from a real capture of https://moodyz.com/works/list/reserve (2026-09-16) —
            // cards sit in a swiper carousel wrapper instead of a plain grid, and there's no
            // per-item date.
            SampleReserveHtml = """
                <div class="p-search__key l-wrap">
                    <p>2026年9月24日〜10月20日発売</p>
                </div>
                <div class="swiper-wrapper">
                    <div class="swiper-slide c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://moodyz.com/works/detail/MIDA791">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/30/content/87749/cover4.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Upcoming Title</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
                """,
            SampleDetailHtml = """
                <div class="swiper-parent">
                    <div class="swiper-container">
                        <div class="swiper-wrapper">
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/30/content/87274/first.jpg" alt="">
                            </div>
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/30/content/87274/second.jpg" alt="">
                            </div>
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/30/content/87274/first.jpg" alt="">
                            </div>
                        </div>
                    </div>
                </div>
                <div class="c-card">
                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/30/content/other/related.jpg" alt="">
                </div>
                <div class="item">
                    <div class="th">女優</div>
                    <div class="td">
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://moodyz.com/actress/detail/713324">八木奈々</a>
                        </div>
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://moodyz.com/actress/detail/859976">佐々木あき</a>
                        </div>
                    </div>
                </div>
                <div class="item">
                    <div class="th">発売日</div>
                    <div class="td">
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://moodyz.com/works/list/date/2026-09-15">2026年9月15日</a>
                        </div>
                    </div>
                </div>
                """,
            FirstCode = "MIRD-281",
            FirstTitle = "Sample Title One",
            FirstCover = "https://cdn.up-timely.com/image/30/content/87274/cover1.jpg",
            FirstDate = new DateTime(2026, 9, 15),
            SecondCode = "MIKR-123",
            ThirdCode = "MIAB-677",
            SecondDate = new DateTime(2026, 9, 8),
            ReserveCode = "MIDA-791",
            ReserveTitle = "Sample Upcoming Title",
            GalleryImage1 = "https://cdn.up-timely.com/image/30/content/87274/first.jpg",
            GalleryImage2 = "https://cdn.up-timely.com/image/30/content/87274/second.jpg",
            Actress1 = "八木奈々",
            Actress2 = "佐々木あき",
            RawCode1 = "MIRD281",
            ExpectedCode1 = "MIRD-281",
            RawCode2 = "MIAB677",
            ExpectedCode2 = "MIAB-677",
            SingleCardReleaseListingHtml = """
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap"><p>2026年9月15日発売</p></div>
                <div class="item"><div class="c-card">
                    <a class="img hover" href="https://moodyz.com/works/detail/MIRD281">
                        <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/30/content/87274/cover.jpg" alt=""/>
                        <div class="hover__child"><p class="text">Sample title</p></div>
                    </a>
                </div></div>
                """,
            SingleCardCode = "MIRD-281",
            SingleCardDetailHtml = """
                <div class="swiper-wrapper">
                    <div class="swiper-slide"><img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/30/content/87274/one.jpg" alt=""></div>
                    <div class="swiper-slide"><img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/30/content/87274/two.jpg" alt=""></div>
                </div>
                """,
            SingleCardGalleryImage1 = "https://cdn.up-timely.com/image/30/content/87274/one.jpg",
            SingleCardGalleryImage2 = "https://cdn.up-timely.com/image/30/content/87274/two.jpg",
        },
        ["Kawaii"] = new()
        {
            BaseUrl = "https://kawaiikawaii.jp",
            SourceName = "Kawaii",
            Studio = "kawaii",
            // Trimmed from a real capture of https://kawaiikawaii.jp/works/list/release
            // (2026-09-16) — two date sections, the second with two cards, to exercise "nearest
            // preceding header" assignment.
            SampleReleaseHtml = """
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap">
                    <p>2026年9月9日発売</p>
                    <p>1作品</p>
                </div>
                <div class="l-wrap">
                    <div class="c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://kawaiikawaii.jp/works/detail/KAVR522">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/32/content/86339/cover1.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Title One</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap">
                    <p>2026年9月2日発売</p>
                    <p>2作品</p>
                </div>
                <div class="l-wrap">
                    <div class="c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://kawaiikawaii.jp/works/detail/CAWB051">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/32/content/2/cover2.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Title Two</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://kawaiikawaii.jp/works/detail/CAWB053">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/32/content/3/cover3.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Title Three</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
                """,
            // Trimmed from a real capture of https://kawaiikawaii.jp/works/list/reserve
            // (2026-09-16) — cards sit in a swiper carousel wrapper instead of a plain grid, and
            // there's no per-item date.
            SampleReserveHtml = """
                <div class="p-search__key l-wrap">
                    <p>2026年9月29日〜10月6日発売</p>
                </div>
                <div class="swiper-wrapper">
                    <div class="swiper-slide c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://kawaiikawaii.jp/works/detail/CAWB060">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/32/content/87531/cover4.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Upcoming Title</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
                """,
            SampleDetailHtml = """
                <div class="swiper-parent">
                    <div class="swiper-container">
                        <div class="swiper-wrapper">
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/32/content/86339/first.jpg" alt="">
                            </div>
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/32/content/86339/second.jpg" alt="">
                            </div>
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/32/content/86339/first.jpg" alt="">
                            </div>
                        </div>
                    </div>
                </div>
                <div class="c-card">
                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/32/content/other/related.jpg" alt="">
                </div>
                <div class="item">
                    <div class="th">女優</div>
                    <div class="td">
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://kawaiikawaii.jp/actress/detail/865774">天音るな</a>
                        </div>
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://kawaiikawaii.jp/actress/detail/858344">渡来ふう</a>
                        </div>
                    </div>
                </div>
                <div class="item">
                    <div class="th">発売日</div>
                    <div class="td">
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://kawaiikawaii.jp/works/list/date/2026-09-09">2026年9月9日</a>
                        </div>
                    </div>
                </div>
                """,
            FirstCode = "KAVR-522",
            FirstTitle = "Sample Title One",
            FirstCover = "https://cdn.up-timely.com/image/32/content/86339/cover1.jpg",
            FirstDate = new DateTime(2026, 9, 9),
            SecondCode = "CAWB-051",
            ThirdCode = "CAWB-053",
            SecondDate = new DateTime(2026, 9, 2),
            ReserveCode = "CAWB-060",
            ReserveTitle = "Sample Upcoming Title",
            GalleryImage1 = "https://cdn.up-timely.com/image/32/content/86339/first.jpg",
            GalleryImage2 = "https://cdn.up-timely.com/image/32/content/86339/second.jpg",
            Actress1 = "天音るな",
            Actress2 = "渡来ふう",
            RawCode1 = "KAVR522",
            ExpectedCode1 = "KAVR-522",
            RawCode2 = "CAWB051",
            ExpectedCode2 = "CAWB-051",
            SingleCardReleaseListingHtml = """
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap"><p>2026年9月9日発売</p></div>
                <div class="item"><div class="c-card">
                    <a class="img hover" href="https://kawaiikawaii.jp/works/detail/KAVR522">
                        <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/32/content/86339/cover.jpg" alt=""/>
                        <div class="hover__child"><p class="text">Sample title</p></div>
                    </a>
                </div></div>
                """,
            SingleCardCode = "KAVR-522",
            SingleCardDetailHtml = """
                <div class="swiper-wrapper">
                    <div class="swiper-slide"><img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/32/content/86339/one.jpg" alt=""></div>
                    <div class="swiper-slide"><img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/32/content/86339/two.jpg" alt=""></div>
                </div>
                """,
            SingleCardGalleryImage1 = "https://cdn.up-timely.com/image/32/content/86339/one.jpg",
            SingleCardGalleryImage2 = "https://cdn.up-timely.com/image/32/content/86339/two.jpg",
        },
        ["IdeaPocket"] = new()
        {
            BaseUrl = "https://ideapocket.com",
            SourceName = "IdeaPocket",
            Studio = "Idea Pocket",
            // Trimmed from a real capture of https://ideapocket.com/works/list/release
            // (2026-09-16) — two date sections, the second with two cards, to exercise "nearest
            // preceding header" assignment.
            SampleReleaseHtml = """
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap">
                    <p>2026年9月15日発売</p>
                    <p>1作品</p>
                </div>
                <div class="l-wrap">
                    <div class="c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://ideapocket.com/works/detail/IPVR379">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/4/content/86364/cover1.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Title One</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap">
                    <p>2026年9月8日発売</p>
                    <p>2作品</p>
                </div>
                <div class="l-wrap">
                    <div class="c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://ideapocket.com/works/detail/IPX901">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/4/content/2/cover2.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Title Two</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://ideapocket.com/works/detail/IPX902">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/4/content/3/cover3.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Title Three</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
                """,
            // Trimmed from a real capture of https://ideapocket.com/works/list/reserve
            // (2026-09-16) — cards sit in a swiper carousel wrapper instead of a plain grid, and
            // there's no per-item date.
            SampleReserveHtml = """
                <div class="p-search__key l-wrap">
                    <p>2026年9月29日〜10月13日発売</p>
                </div>
                <div class="swiper-wrapper">
                    <div class="swiper-slide c-low--6">
                        <div class="item">
                            <div class="c-card">
                                <a class="img hover" href="https://ideapocket.com/works/detail/IPX910">
                                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/4/content/86500/cover4.jpg" alt=""/>
                                    <div class="hover__child">
                                        <p class="text">Sample Upcoming Title</p>
                                    </div>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
                """,
            SampleDetailHtml = """
                <div class="swiper-parent">
                    <div class="swiper-container">
                        <div class="swiper-wrapper">
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/4/content/86364/first.jpg" alt="">
                            </div>
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/4/content/86364/second.jpg" alt="">
                            </div>
                            <div class="swiper-slide">
                                <img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/4/content/86364/first.jpg" alt="">
                            </div>
                        </div>
                    </div>
                </div>
                <div class="c-card">
                    <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/4/content/other/related.jpg" alt="">
                </div>
                <div class="item">
                    <div class="th">女優</div>
                    <div class="td">
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://ideapocket.com/actress/detail/863159">坂井美桜</a>
                        </div>
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://ideapocket.com/actress/detail/111111">第二女優</a>
                        </div>
                    </div>
                </div>
                <div class="item">
                    <div class="th">発売日</div>
                    <div class="td">
                        <div class="item">
                            <a class="c-tag c-main-bg-hover c-main-font c-main-bd" href="https://ideapocket.com/works/list/date/2026-09-15">2026年9月15日</a>
                        </div>
                    </div>
                </div>
                """,
            FirstCode = "IPVR-379",
            FirstTitle = "Sample Title One",
            FirstCover = "https://cdn.up-timely.com/image/4/content/86364/cover1.jpg",
            FirstDate = new DateTime(2026, 9, 15),
            SecondCode = "IPX-901",
            ThirdCode = "IPX-902",
            SecondDate = new DateTime(2026, 9, 8),
            ReserveCode = "IPX-910",
            ReserveTitle = "Sample Upcoming Title",
            GalleryImage1 = "https://cdn.up-timely.com/image/4/content/86364/first.jpg",
            GalleryImage2 = "https://cdn.up-timely.com/image/4/content/86364/second.jpg",
            Actress1 = "坂井美桜",
            Actress2 = "第二女優",
            RawCode1 = "IPVR379",
            ExpectedCode1 = "IPVR-379",
            RawCode2 = "IPX901",
            ExpectedCode2 = "IPX-901",
            SingleCardReleaseListingHtml = """
                <div class="p-search__key c-main-bd-bottom -flexBet l-wrap"><p>2026年9月15日発売</p></div>
                <div class="item"><div class="c-card">
                    <a class="img hover" href="https://ideapocket.com/works/detail/IPVR379">
                        <img class="c-main-bg lazyload" data-src="https://cdn.up-timely.com/image/4/content/86364/cover.jpg" alt=""/>
                        <div class="hover__child"><p class="text">Sample title</p></div>
                    </a>
                </div></div>
                """,
            SingleCardCode = "IPVR-379",
            SingleCardDetailHtml = """
                <div class="swiper-wrapper">
                    <div class="swiper-slide"><img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/4/content/86364/one.jpg" alt=""></div>
                    <div class="swiper-slide"><img class="swiper-lazy" data-src="https://cdn.up-timely.com/image/4/content/86364/two.jpg" alt=""></div>
                </div>
                """,
            SingleCardGalleryImage1 = "https://cdn.up-timely.com/image/4/content/86364/one.jpg",
            SingleCardGalleryImage2 = "https://cdn.up-timely.com/image/4/content/86364/two.jpg",
        },
    };
}
