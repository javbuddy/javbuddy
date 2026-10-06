using System.Net;
using Javbuddy.Services.MovieDiscovery.Sources;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.MovieDiscovery.Sources;

public class UpTimelyDiscoverySourceTests
{
    public static IEnumerable<object[]> SiteKeys => UpTimelySiteFixtures.All.Keys.Select(key => new object[] { key });

    [Theory]
    [MemberData(nameof(SiteKeys))]
    public async Task ScanAsync_FetchesEachCandidateDetailPageAndReturnsItsGalleryImages(string site)
    {
        var fixture = UpTimelySiteFixtures.All[site];
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var content = request.RequestUri!.AbsolutePath switch
            {
                "/works/list/release" => fixture.SingleCardReleaseListingHtml,
                "/works/list/reserve" => "",
                var path when path == $"/works/detail/{fixture.SingleCardCode.Replace("-", string.Empty)}" => fixture.SingleCardDetailHtml,
                _ => throw new InvalidOperationException($"Unexpected URL: {request.RequestUri}"),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        });
        var source = new UpTimelyDiscoverySource(
            fixture.BaseUrl,
            fixture.SourceName,
            logo: null,
            fixture.Studio,
            new FakeHttpClientFactory(handler));

        var item = Assert.Single(await source.ScanAsync());

        Assert.Equal(fixture.SingleCardCode, item.Code);
        Assert.Equal([fixture.SingleCardGalleryImage1, fixture.SingleCardGalleryImage2], item.GalleryImageUrls);
    }
}
