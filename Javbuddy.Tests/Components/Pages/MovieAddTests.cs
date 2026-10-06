using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Services.Movies;
using Javbuddy.Services.R18Dev;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

/// <summary>Add New's r18.dev catalog suggestions under the code box.</summary>
public class MovieAddTests : BunitContext
{
    private readonly IR18DevReleaseBrowseService catalog = Substitute.For<IR18DevReleaseBrowseService>();

    public MovieAddTests()
    {
        Services.AddSingleton(Substitute.For<IMovieAddService>());
        Services.AddSingleton(catalog);
        catalog.SuggestAsync(default!, default, default).ReturnsForAnyArgs(Array.Empty<R18DevReleaseRow>());
    }

    private static R18DevReleaseRow Row(string code, string statusClass = "missing") =>
        new(new R18DevFilmographyEntry(code, "Title " + code, null, new DateOnly(2024, 2, 3), null),
            statusClass, statusClass == "missing" ? "2024-02-03" : "Got", $"/movies/{code}", code);

    [Fact]
    public void TypingACode_ListsCatalogMatches_AndPickingOneFillsTheBox()
    {
        catalog.SuggestAsync("ssis-00", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Row("SSIS-001"), Row("SSIS-002", "got") });
        var cut = Render<MovieAdd>();

        cut.Find("input.form-control").Input("ssis-00");

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("button.add-suggestion").Count), TimeSpan.FromSeconds(5));
        Assert.Contains("2024-02-03", cut.FindAll("button.add-suggestion")[0].TextContent);
        Assert.Contains("In library (Got)", cut.FindAll("button.add-suggestion")[1].TextContent);

        cut.FindAll("button.add-suggestion")[0].Click();

        Assert.Equal("SSIS-001", cut.Find("input.form-control").GetAttribute("value"));
        Assert.Empty(cut.FindAll("button.add-suggestion"));
    }

    [Fact]
    public void ClearingTheBox_HidesTheSuggestions()
    {
        catalog.SuggestAsync("ssis", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[] { Row("SSIS-001") });
        var cut = Render<MovieAdd>();
        cut.Find("input.form-control").Input("ssis");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button.add-suggestion")), TimeSpan.FromSeconds(5));

        cut.Find("input.form-control").Input("");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("button.add-suggestion")));
    }
}
