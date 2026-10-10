using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tags;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class MovieTagsTests : BunitContext
{
    [Fact]
    public async Task DeletingATagWithSubtags_ShowsAnErrorInsteadOfThrowing()
    {
        using var factory = new TestDbContextFactory();
        var tagService = new TagService(factory, Substitute.For<INfoSyncService>());
        var parent = await tagService.CreateTagAsync("Cosplay");
        await tagService.CreateTagAsync("ram", parent.Tag!.Id);
        Services.AddSingleton<ITagService>(tagService);
        Services.AddSingleton(Substitute.For<ITagRuleService>());
        Services.AddSingleton(Substitute.For<IActorTagService>());
        Services.AddSingleton(new MovieFilterNavigationState());

        var cut = Render<MovieTags>();
        cut.WaitForState(() => cut.Markup.Contains("Select Cosplay"));
        cut.FindAll("tr").First(r => r.QuerySelector("input[aria-label='Select Cosplay']") is not null)
            .QuerySelector("button[title='Delete']")!.Click();
        cut.Find("button.btn-danger").Click();

        Assert.Contains("subtags", cut.Find(".alert-danger").TextContent);
        Assert.NotNull(await tagService.GetByIdAsync(parent.Tag.Id));
    }
}
