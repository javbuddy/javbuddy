using AngleSharp.Html.Dom;
using Bunit;
using Javbuddy.Components.Pages.SettingsMetadataSections;
using Javbuddy.Models;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Javbuddy.Tests.Components.Pages.SettingsMetadataSections;

public class TrickplaySectionTests : BunitContext
{
    private readonly TestDbContextFactory dbFactory = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) dbFactory.Dispose();
    }

    private ITrickplaySettingsService UseSettings(Dictionary<string, string?>? env = null)
    {
        var service = new TrickplaySettingsService(dbFactory, new ConfigurationBuilder().AddInMemoryCollection(env ?? []).Build());
        Services.AddSingleton<ITrickplaySettingsService>(service);
        return service;
    }

    [Fact]
    public async Task SavingTheForm_SavesIt_AndTellsThePage()
    {
        var service = UseSettings();
        var savedCount = 0;
        var cut = Render<TrickplaySection>(p => p.Add(x => x.OnSaved, () => savedCount++));

        ((IHtmlInputElement)cut.Find("#trickplay-generate-new")).Change(false);
        cut.Find("form").Submit();

        Assert.False((await service.GetEffectiveAsync()).GenerateForNewFiles);
        Assert.Equal(1, savedCount);
        Assert.Contains("Saved.", cut.Markup);
    }

    [Fact]
    public async Task WhenSetByItsEnvironmentVariable_ShowsThatValue_ReadOnly()
    {
        var service = UseSettings(new() { ["Trickplay:KeyframeOnly"] = "true" });
        await service.SaveAsync(new TrickplaySettings { KeyframeOnly = false });

        var cut = Render<TrickplaySection>();

        var checkbox = (IHtmlInputElement)cut.Find("#trickplay-keyframe-only");
        Assert.True(checkbox.IsChecked);
        Assert.True(checkbox.HasAttribute("disabled"));
        Assert.Contains("Trickplay__KeyframeOnly", cut.Find(".trickplay-env-note").TextContent);
        // The other fields can still be saved.
        Assert.False(cut.Find("#trickplay-generate-new").HasAttribute("disabled"));
        Assert.False(cut.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task SavingTheForm_KeepsTheJellyfinFallback()
    {
        // The fallback toggle lives in Settings > Connections > Jellyfin.
        var service = UseSettings();
        await service.SaveAsync(new TrickplaySettings { JellyfinFallback = false });
        var cut = Render<TrickplaySection>();
        Assert.Empty(cut.FindAll("#trickplay-jellyfin-fallback"));

        cut.Find("form").Submit();

        Assert.False((await service.GetEffectiveAsync()).JellyfinFallback);
    }

    [Fact]
    public void WhenEveryFieldIsSetByTheEnvironment_ThereIsNothingToSave()
    {
        UseSettings(new()
        {
            ["Trickplay:GenerateForNewFiles"] = "true",
            ["Trickplay:KeyframeOnly"] = "false",
        });

        var cut = Render<TrickplaySection>();

        Assert.Equal(2, cut.FindAll(".trickplay-env-note").Count);
        Assert.True(cut.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task KeyframeOnly_IsOffByDefault_AndCanBeTurnedOn()
    {
        var service = UseSettings();
        var cut = Render<TrickplaySection>();
        var keyframes = (IHtmlInputElement)cut.Find("#trickplay-keyframe-only");
        Assert.False(keyframes.IsChecked);
        Assert.True(((IHtmlInputElement)cut.Find("#trickplay-generate-new")).IsChecked);

        keyframes.Change(true);
        cut.Find("form").Submit();

        Assert.True((await service.GetEffectiveAsync()).KeyframeOnly);
    }
}
