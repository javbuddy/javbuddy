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

/// <summary>Covers the "libraries to check" list's env-vs-DB editability split: when the
/// selection is env-configured, the list is still shown but read-only —
/// distinct from SectionSmokeTests, which only checks the section renders at all.</summary>
public class JellyfinConnectionSectionLibrarySelectionTests : BunitContext
{
    private static readonly List<JellyfinVirtualFolderDto> Libraries =
    [
        new JellyfinVirtualFolderDto { Name = "JAV", ItemId = "id-1", CollectionType = "movies" },
        new JellyfinVirtualFolderDto { Name = "West", ItemId = "id-2", CollectionType = "movies" },
    ];

    private IJellyfinClient SetUpServices(IConfiguration configuration)
    {
        var dbFactory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(dbFactory);
        Services.AddSingleton<IConnectionSettingsSaveService>(new ConnectionSettingsSaveService(dbFactory));
        var trickplaySettings = Substitute.For<ITrickplaySettingsService>();
        trickplaySettings.GetEffectiveAsync(Arg.Any<CancellationToken>()).Returns(new TrickplaySettings());
        Services.AddSingleton(trickplaySettings);
        Services.AddSingleton(configuration);

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.GetLibrariesAsync(Arg.Any<CancellationToken>())
            .Returns(new JellyfinLibrariesResult(true, Libraries, null));
        Services.AddSingleton(jellyfinClient);
        return jellyfinClient;
    }

    [Fact]
    public void WhenSelectionIsEnvConfigured_CheckboxesAreDisabledAndSaveButtonIsHidden()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jellyfin:SelectedLibraryNames"] = "JAV" })
            .Build();
        SetUpServices(configuration);

        var cut = Render<JellyfinConnectionSection>();
        cut.FindAll("button").First(b => b.TextContent.Contains("Load libraries")).Click();

        cut.WaitForAssertion(() =>
        {
            var checkboxes = cut.FindAll("input[id^='lib-']");
            Assert.Equal(2, checkboxes.Count);
            Assert.All(checkboxes, c => Assert.True(c.HasAttribute("disabled")));
            Assert.DoesNotContain("Save library selection", cut.Markup);
        });
    }

    [Fact]
    public void WhenSelectionIsDbBacked_CheckboxesAreEnabledAndSaveButtonIsShown()
    {
        var configuration = new ConfigurationBuilder().Build();
        SetUpServices(configuration);

        var cut = Render<JellyfinConnectionSection>();
        cut.FindAll("button").First(b => b.TextContent.Contains("Load libraries")).Click();

        cut.WaitForAssertion(() =>
        {
            var checkboxes = cut.FindAll("input[id^='lib-']");
            Assert.Equal(2, checkboxes.Count);
            Assert.All(checkboxes, c => Assert.False(c.HasAttribute("disabled")));
            Assert.Contains("Save library selection", cut.Markup);
        });
    }
}
