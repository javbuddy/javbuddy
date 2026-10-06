using Javbuddy.Services.Images;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Images;

public class LocalImageCacheSettingsTests
{
    [Fact]
    public void FromConfiguration_Unset_UsesLocalMoviesWithoutExpiryOrSizeLimit()
    {
        var settings = ImageCacheSettings.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Equal(ImageCacheMode.LocalMovies, settings.Mode);
        Assert.Null(settings.Ttl);
        Assert.Null(settings.MaxSizeBytes);
    }

    [Fact]
    public void FromConfiguration_ValidValues_ParsesModeTtlAndMaxSize()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ImageCache:Mode"] = "Thumbnails",
            ["ImageCache:TtlHours"] = "12",
            ["ImageCache:MaxSizeMb"] = "256",
        }).Build();

        var settings = ImageCacheSettings.FromConfiguration(configuration);

        Assert.Equal(ImageCacheMode.Thumbnails, settings.Mode);
        Assert.Equal(TimeSpan.FromHours(12), settings.Ttl);
        Assert.Equal(256L * 1024 * 1024, settings.MaxSizeBytes);
    }

    [Fact]
    public void FromConfiguration_InvalidOrNonPositiveLimits_DisablesLimits()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ImageCache:Mode"] = "not-a-mode",
            ["ImageCache:TtlHours"] = "0",
            ["ImageCache:MaxSizeMb"] = "-1",
        }).Build();

        var settings = ImageCacheSettings.FromConfiguration(configuration);

        Assert.Equal(ImageCacheMode.LocalMovies, settings.Mode);
        Assert.Null(settings.Ttl);
        Assert.Null(settings.MaxSizeBytes);
    }

    [Fact]
    public void FromConfiguration_MaxSizeMbOverflowsLongBytes_FallsBackToUnlimited()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ImageCache:MaxSizeMb"] = "9999999999999999",
        }).Build();

        var settings = ImageCacheSettings.FromConfiguration(configuration);

        Assert.Null(settings.MaxSizeBytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LocalImageCacheEnvConfig_GetPath_ReturnsNullWhenUnsetOrWhitespace(string? path)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ImageCache:Path"] = path,
            })
            .Build();

        Assert.Null(LocalImageCacheEnvConfig.GetPath(config));
    }
}
