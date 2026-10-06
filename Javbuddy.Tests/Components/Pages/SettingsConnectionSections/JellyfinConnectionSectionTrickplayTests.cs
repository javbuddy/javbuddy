using AngleSharp.Html.Dom;
using Bunit;
using Javbuddy.Components.Pages.SettingsConnectionSections;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.SettingsConnectionSections;

/// <summary>The "use Jellyfin's trickplay until a movie has its own" toggle, moved here from
/// Settings > Metadata > Trickplay.</summary>
public class JellyfinConnectionSectionTrickplayTests : BunitContext
{
    private readonly TestDbContextFactory dbFactory = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) dbFactory.Dispose();
    }

    private ITrickplaySettingsService SetUpServices(Dictionary<string, string?>? env = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(env ?? []).Build();
        var service = new TrickplaySettingsService(dbFactory, configuration);
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(dbFactory);
        Services.AddSingleton<IConnectionSettingsSaveService>(new ConnectionSettingsSaveService(dbFactory));
        Services.AddSingleton(Substitute.For<IJellyfinClient>());
        Services.AddSingleton<IConfiguration>(configuration);
        Services.AddSingleton<ITrickplaySettingsService>(service);
        return service;
    }

    [Fact]
    public async Task TurningTheFallbackOff_SavesIt_AndKeepsTheOtherTrickplayFields()
    {
        var service = SetUpServices();
        await service.SaveAsync(new TrickplaySettings { GenerateForNewFiles = false, KeyframeOnly = true, JellyfinFallback = true });
        var cut = Render<JellyfinConnectionSection>();
        var checkbox = (IHtmlInputElement)cut.Find("#trickplay-jellyfin-fallback");
        Assert.True(checkbox.IsChecked);
        Assert.False(checkbox.HasAttribute("disabled"));

        checkbox.Change(false);
        cut.FindAll("button").First(b => b.TextContent.Contains("Save trickplay setting")).Click();

        var stored = await service.GetStoredAsync();
        Assert.Equal((false, true, false), (stored.GenerateForNewFiles, stored.KeyframeOnly, stored.JellyfinFallback));
        Assert.Contains("Saved.", cut.Markup);
    }

    [Fact]
    public async Task WhenSetByItsEnvironmentVariable_ShowsThatValue_ReadOnly()
    {
        var service = SetUpServices(new() { ["Trickplay:JellyfinFallback"] = "false" });
        await service.SaveAsync(new TrickplaySettings { JellyfinFallback = true });

        var cut = Render<JellyfinConnectionSection>();

        var checkbox = (IHtmlInputElement)cut.Find("#trickplay-jellyfin-fallback");
        Assert.False(checkbox.IsChecked);
        Assert.True(checkbox.HasAttribute("disabled"));
        Assert.Contains("Trickplay__JellyfinFallback", cut.Find(".trickplay-env-note").TextContent);
        Assert.DoesNotContain("Save trickplay setting", cut.Markup);
    }
}
