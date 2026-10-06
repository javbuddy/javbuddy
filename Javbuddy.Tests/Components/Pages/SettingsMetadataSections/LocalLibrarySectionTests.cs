using Bunit;
using Javbuddy.Components.Pages.SettingsMetadataSections;
using Javbuddy.Data;
using Javbuddy.Services.Settings;
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

        var cut = Render<LocalLibrarySection>();

        Assert.Contains("Local Library", cut.Find(".card-title").TextContent);
    }
}
