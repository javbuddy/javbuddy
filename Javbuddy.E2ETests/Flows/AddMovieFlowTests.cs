using System.Text.RegularExpressions;
using Javbuddy.E2ETests.Fixtures;
using Javbuddy.E2ETests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class AddMovieFlowTests
{
    private readonly E2EFixture fixture;

    public AddMovieFlowTests(E2EFixture fixture)
    {
        this.fixture = fixture;
        fixture.ResetFakes();
    }

    [Fact]
    public async Task AddByCode_ScrapesMetadataFromJavinizerAndLandsOnMovieDetail()
    {
        const string code = "E2E-001";
        var page = await fixture.NewPageAsync();

        await page.GotoInteractiveAsync("/add/new");
        await page.GetByPlaceholder("Code, e.g. IPX-535").FillAsync(code);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex($"/movies/{Regex.Escape(code)}$"));
        // Applied titles follow the "{code} {title}" convention (MovieMetadataMapper.ResolveTitle).
        await Expect(page).ToHaveTitleAsync($"{code} Test Movie {code} · Javbuddy");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = $"{code} Test Movie {code}", Exact = true })).ToBeVisibleAsync();
    }
}
