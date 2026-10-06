using Javbuddy.Services.Images;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Images;

public class ImageUploadSettingsTests
{
    [Fact]
    public void Default_HasExpectedDefaults()
    {
        var settings = ImageUploadSettings.Default;

        Assert.Equal(20, settings.MaxSizeMb);
        Assert.Equal(20L * 1024L * 1024L, settings.MaxSizeBytes);
        Assert.Equal(500, settings.MaxBatchFiles);
    }

    [Fact]
    public void FromConfiguration_WhenMissing_ReturnsDefault()
    {
        var configuration = new ConfigurationBuilder().Build();

        var settings = ImageUploadSettings.FromConfiguration(configuration);

        Assert.Equal(20, settings.MaxSizeMb);
        Assert.Equal(20L * 1024L * 1024L, settings.MaxSizeBytes);
        Assert.Equal(500, settings.MaxBatchFiles);
    }

    [Theory]
    [InlineData("50", 50)]
    [InlineData("100", 100)]
    [InlineData("1", 1)]
    public void FromConfiguration_WhenValidPositiveInt_ParsesCorrectly(string configuredValue, int expectedMb)
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["ImageUpload:MaxSizeMb"] = configuredValue,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var settings = ImageUploadSettings.FromConfiguration(configuration);

        Assert.Equal(expectedMb, settings.MaxSizeMb);
        Assert.Equal((long)expectedMb * 1024L * 1024L, settings.MaxSizeBytes);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    public void FromConfiguration_WhenInvalidOrNonPositive_FallsBackToDefault(string invalidValue)
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["ImageUpload:MaxSizeMb"] = invalidValue,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var settings = ImageUploadSettings.FromConfiguration(configuration);

        Assert.Equal(ImageUploadSettings.DefaultMaxSizeMb, settings.MaxSizeMb);
        Assert.Equal(ImageUploadSettings.Default.MaxSizeBytes, settings.MaxSizeBytes);
    }

    [Theory]
    [InlineData("50", 50)]
    [InlineData("200", 200)]
    [InlineData("1000", 1000)]
    public void FromConfiguration_WhenBatchFilesValidPositiveInt_ParsesCorrectly(string configuredValue, int expectedFiles)
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["ImageUpload:MaxBatchFiles"] = configuredValue,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var settings = ImageUploadSettings.FromConfiguration(configuration);

        Assert.Equal(expectedFiles, settings.MaxBatchFiles);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("xyz")]
    [InlineData("")]
    [InlineData("   ")]
    public void FromConfiguration_WhenBatchFilesInvalidOrNonPositive_FallsBackToDefault(string invalidValue)
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["ImageUpload:MaxBatchFiles"] = invalidValue,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var settings = ImageUploadSettings.FromConfiguration(configuration);

        Assert.Equal(ImageUploadSettings.DefaultMaxBatchFiles, settings.MaxBatchFiles);
    }
}
