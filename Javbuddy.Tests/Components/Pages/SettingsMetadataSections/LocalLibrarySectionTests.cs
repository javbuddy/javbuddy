using Bunit;
using Javbuddy.Components.Pages.SettingsMetadataSections;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.SettingsMetadataSections;

public class LocalLibrarySectionTests : BunitContext
{
    [Fact]
    public void LocalLibrarySection_RendersTitle()
    {
        using var dbFactory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(dbFactory);
        Services.AddSingleton<IConnectionSettingsSaveService>(new ConnectionSettingsSaveService(dbFactory));
        Services.AddSingleton(Substitute.For<IConfiguration>());
        Services.AddSingleton<IMediaInfoSettingsService>(new MediaInfoSettingsService(dbFactory));
        var trickplay = Substitute.For<ITrickplaySettingsService>();
        trickplay.GetStoredAsync(Arg.Any<CancellationToken>()).Returns(new TrickplaySettings());
        trickplay.GetEffectiveAsync(Arg.Any<CancellationToken>()).Returns(new TrickplaySettings());
        Services.AddSingleton(trickplay);
        var artwork = Substitute.For<INewMovieArtworkSettingsService>();
        artwork.GetStoredAsync(Arg.Any<CancellationToken>()).Returns(new NewMovieArtworkSettings());
        artwork.GetEffectiveAsync(Arg.Any<CancellationToken>()).Returns(new NewMovieArtworkSettings());
        Services.AddSingleton(artwork);

        var cut = Render<LocalLibrarySection>();

        Assert.Equal(
            ["Local Library", "MediaInfo", "Trickplay", "New movie artwork"],
            cut.FindAll(".card-title").Select(t => t.TextContent));
    }
}
