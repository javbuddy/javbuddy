using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.DeoVr;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Javbuddy.Tests.Components.Pages;

public class SettingsDeoVrTests : BunitContext
{
    private readonly TestDbContextFactory dbFactory = new();

    public SettingsDeoVrTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(dbFactory);
        Services.AddSingleton<IDeoVrGroupService>(new DeoVrGroupService(dbFactory));
        Services.AddSingleton<IMovieGridQueryService>(new MovieGridQueryService(dbFactory));
        Services.AddSingleton(TimeProvider.System);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) dbFactory.Dispose();
        base.Dispose(disposing);
    }

    private IRenderedComponent<SettingsDeoVr> RenderPage(Dictionary<string, string?>? env = null)
    {
        Services.AddSingleton<IDeoVrSettingsService>(
            new DeoVrSettingsService(dbFactory, new ConfigurationBuilder().AddInMemoryCollection(env ?? []).Build()));
        var cut = Render<SettingsDeoVr>();
        cut.WaitForElement("#deovr-enabled");
        return cut;
    }

    [Fact]
    public async Task SavingTheForm_EnablesDeoVr_AndShowsTheAddressToOpen()
    {
        var cut = RenderPage();
        Assert.False(cut.Find("#deovr-enabled").HasAttribute("checked"));
        Assert.DoesNotContain("In DeoVR, open", cut.Markup, StringComparison.Ordinal);

        cut.Find("#deovr-enabled").Change(true);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/deovr", cut.Find(".deovr-address").TextContent));
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = Assert.Single(db.DeoVrSettings);
        Assert.True(row.Enabled);
        Assert.Empty(cut.FindAll("#deovr-public-url"));
    }

    [Fact]
    public void AFieldSetByTheEnvironment_IsReadOnlyWithANote()
    {
        var cut = RenderPage(new() { ["DeoVr:Enabled"] = "true" });

        var toggle = cut.Find("#deovr-enabled");
        Assert.True(toggle.HasAttribute("disabled"));
        Assert.True(toggle.HasAttribute("checked"));
        Assert.Contains("DeoVr__Enabled", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task AddingAGroup_SavesItWithTheChosenActor_AndListsIt()
    {
        int amyId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var amy = new Actor { FirstName = "Amy" };
            var mide = new Movie { Code = "MIDE-400", Status = MovieStatus.Got };
            var ssni = new Movie { Code = "SSNI-081", Status = MovieStatus.Got };
            mide.MovieActors.Add(new MovieActor { Actor = amy });
            ssni.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Bea" } });
            db.Movies.AddRange(mide, ssni, new Movie { Code = "ABC-001", Status = MovieStatus.Missing });
            await db.SaveChangesAsync();
            db.MovieFiles.AddRange(
                new MovieFile { MovieId = mide.Id, FileName = "MIDE-400.mkv", IsPrimary = true },
                new MovieFile { MovieId = ssni.Id, FileName = "SSNI-081.mkv", IsPrimary = true });
            await db.SaveChangesAsync();
            amyId = amy.Id;
        }
        var cut = RenderPage();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Add group", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains("2 Got movies with a local file match.", cut.Find(".deovr-match-count").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".deovr-editor .btn-group")); // no All/Missing/Got buttons
        cut.Find("#deovr-group-name").Input("Amy");
        cut.FindAll(".deovr-editor button").First(b => b.TextContent.StartsWith("Filter", StringComparison.Ordinal)).Click();
        cut.FindAll(".sort-dropdown-item").Single(b => b.TextContent.Trim().StartsWith("Actor", StringComparison.Ordinal)).Click();
        cut.FindAll(".sort-dropdown-item").Single(b => b.TextContent.Trim().StartsWith("Name", StringComparison.Ordinal)).Click();
        cut.FindAll(".sort-dropdown-subitem").Single(b => b.TextContent.Trim() == "Amy").Click();
        cut.WaitForAssertion(() => Assert.Contains("1 Got movie with a local file match.", cut.Find(".deovr-match-count").TextContent, StringComparison.Ordinal));
        cut.FindAll("button").Single(b => b.TextContent == "Save group").Click();

        cut.WaitForAssertion(() => Assert.Contains("Amy", cut.FindAll(".deovr-group")[1].TextContent, StringComparison.Ordinal));
        var group = (await new DeoVrGroupService(dbFactory).ListAsync())[1];
        Assert.Equal(("Amy", null), (group.Name, group.Filter.Status));
        Assert.Equal([amyId], group.Filter.ActorIds!);
        Assert.Empty(cut.FindAll(".deovr-editor"));
    }

    [Fact]
    public async Task WithEveryGroupDeleted_WarnsThatDeoVrListsNothing()
    {
        await new DeoVrGroupService(dbFactory).DeleteAsync(DeoVrGroup.AllMovies.Id);

        var cut = RenderPage();

        Assert.Contains("DeoVR has nothing to list", cut.Find(".deovr-no-groups").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".deovr-group"));
    }

    [Fact]
    public void SavingAGroupWithoutAName_ShowsAnError()
    {
        var cut = RenderPage();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Add group", StringComparison.Ordinal)).Click();
        cut.FindAll("button").Single(b => b.TextContent == "Save group").Click();

        Assert.Contains("Give the group a name.", cut.Find(".alert-danger").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletingAGroup_AsksFirst_ThenRemovesIt()
    {
        var groups = new DeoVrGroupService(dbFactory);
        await groups.SaveAsync(0, "B", new MovieGridFilter(), "added", true);
        var cut = RenderPage();
        Assert.Equal("All movies", cut.FindAll(".deovr-group")[0].TextContent.Trim()[..10]);

        cut.FindAll(".deovr-group")[0].QuerySelectorAll("button").Single(b => b.TextContent == "Delete").Click();
        cut.Find(".delete-confirm-confirm-btn").Click();

        cut.WaitForAssertion(() => Assert.Equal(["B"], cut.FindAll(".deovr-group").Select(g => g.TextContent.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0])));
        Assert.Equal(["B"], (await groups.ListAsync()).Select(g => g.Name));
    }
}
