using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Fixtures.FakeServices;
using Javbuddy.E2ETests.Support;
using Javbuddy.Services.Javinizer;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class TorrentSortPosterFlowTests(E2EFixture fixture)
{
    [Theory]
    [InlineData(false, "cover")]
    [InlineData(true, "contain")]
    public async Task ReviewAndPreview_RespectPosterSelection(bool customPoster, string expectedFit)
    {
        fixture.ResetFakes();
        var code = $"E2E-POSTER-{customPoster}";
        var movie = await DbSeeding.SeedMovieAsync(fixture.DbFactory, code);
        var download = await DbSeeding.SeedTorrentDownloadAsync(fixture.DbFactory, movie);
        await using (var db = await fixture.DbFactory.CreateDbContextAsync())
        {
            var row = await db.TorrentDownloads.FindAsync(download.Id);
            row!.JavinizerBatchJobId = code;
            await db.SaveChangesAsync();
        }

        var metadata = FakeJavinizerServer.BuildDefaultMovie(code);
        metadata.PosterUrl = $"{fixture.App.ServerAddress}/e2e-poster.svg";
        metadata.OriginalPosterUrl = customPoster ? "https://example.test/default.jpg" : metadata.PosterUrl;
        // A false backend flag must not disable cropping of an unchanged default poster.
        metadata.ShouldCropPoster = false;
        fixture.FakeJavinizer.BatchResponses[code] = new BatchJobResponseDto
        {
            Id = code,
            Status = JavinizerJobStatus.Completed,
            TotalFiles = 1,
            Completed = 1,
            Destination = "/synthetic-library",
            Results = new()
            {
                [code] = new BatchFileResultDto
                {
                    ResultId = code,
                    FilePath = $"/synthetic-downloads/{code}.mp4",
                    Status = JavinizerJobStatus.Completed,
                    Movie = metadata,
                },
            },
        };

        var page = await fixture.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        try
        {
            await page.RouteAsync("**/e2e-poster.svg", route => route.FulfillAsync(new()
            {
                ContentType = "image/svg+xml",
                Body = "<svg xmlns='http://www.w3.org/2000/svg' width='600' height='300'><rect width='300' height='300' fill='blue'/><rect x='300' width='300' height='300' fill='orange'/></svg>",
            }));
            await page.GotoInteractiveAsync($"/activity/sort/{download.Id}");
            var reviewPoster = page.Locator(".sort-movie-poster-img");
            await Expect(reviewPoster).ToHaveCSSAsync("object-fit", expectedFit);
            await page.WaitForFunctionAsync("() => document.querySelector('.sort-movie-poster-img')?.naturalWidth === 600");

            await page.GetByRole(AriaRole.Button, new() { Name = "Continue to Preview" }).ClickAsync();
            var previewPoster = page.Locator(".sort-preview-thumb");
            await Expect(previewPoster).ToHaveCSSAsync("object-fit", expectedFit);
            await page.WaitForFunctionAsync("() => document.querySelector('.sort-preview-thumb')?.naturalWidth === 600");
            await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
            Assert.Empty(pageErrors);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
