using AngleSharp.Html.Dom;
using Bunit;
using Javbuddy.Components.Pages.SettingsMetadataSections;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Javbuddy.Tests.Components.Pages.SettingsMetadataSections;

public class NewMovieArtworkSectionTests : BunitContext
{
    private readonly TestDbContextFactory dbFactory = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) dbFactory.Dispose();
    }

    private INewMovieArtworkSettingsService UseSettings(Dictionary<string, string?>? env = null)
    {
        var service = new NewMovieArtworkSettingsService(dbFactory, new ConfigurationBuilder().AddInMemoryCollection(env ?? []).Build());
        Services.AddSingleton<INewMovieArtworkSettingsService>(service);
        return service;
    }

    [Fact]
    public void BothTogglesStartOn()
    {
        UseSettings();

        var cut = Render<NewMovieArtworkSection>();

        Assert.True(((IHtmlInputElement)cut.Find("#new-artwork-extrafanart")).IsChecked);
        Assert.True(((IHtmlInputElement)cut.Find("#new-artwork-poster")).IsChecked);
    }

    [Fact]
    public async Task SavingTheForm_SavesIt_AndTellsThePage()
    {
        var service = UseSettings();
        var savedCount = 0;
        var cut = Render<NewMovieArtworkSection>(p => p.Add(x => x.OnSaved, () => savedCount++));

        ((IHtmlInputElement)cut.Find("#new-artwork-extrafanart")).Change(false);
        cut.Find("form").Submit();

        var effective = await service.GetEffectiveAsync();
        Assert.False(effective.GenerateExtraFanart);
        Assert.True(effective.GeneratePosterAndFanart);
        Assert.Equal(1, savedCount);
        Assert.Contains("Saved.", cut.Markup);
    }

    [Fact]
    public void WhenSetByItsEnvironmentVariable_ShowsThatValue_ReadOnly()
    {
        UseSettings(new() { ["NewMovieArtwork:GeneratePosterAndFanart"] = "false" });

        var cut = Render<NewMovieArtworkSection>();

        var checkbox = (IHtmlInputElement)cut.Find("#new-artwork-poster");
        Assert.False(checkbox.IsChecked);
        Assert.True(checkbox.HasAttribute("disabled"));
        Assert.Contains("NewMovieArtwork__GeneratePosterAndFanart", cut.Markup);
        Assert.False(cut.Find("#new-artwork-extrafanart").HasAttribute("disabled"));
        Assert.False(cut.Find("button[type=submit]").HasAttribute("disabled"));
    }
}
