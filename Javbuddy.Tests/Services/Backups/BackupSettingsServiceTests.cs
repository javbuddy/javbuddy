using Javbuddy.Models;

namespace Javbuddy.Tests.Services.Backups;

public sealed class BackupSettingsServiceTests
{
    [Fact]
    public async Task GetEffectiveAsync_UsesDefaultsWhenNothingIsSaved()
    {
        using var env = new BackupTestEnvironment();

        var settings = await env.Settings.GetEffectiveAsync();

        Assert.True(settings.Enabled);
        Assert.Equal(10, settings.RetentionDays);
        Assert.Equal(1, settings.FrequencyDays);
    }

    [Fact]
    public async Task SaveAsync_PersistsAndRefreshesTheCachedValue()
    {
        using var env = new BackupTestEnvironment();
        Assert.Equal(1, env.Settings.GetEffectiveCached().FrequencyDays);

        await env.Settings.SaveAsync(new BackupSettings { Enabled = false, RetentionDays = 30, FrequencyDays = 7 });

        var cached = env.Settings.GetEffectiveCached();
        Assert.False(cached.Enabled);
        Assert.Equal(30, cached.RetentionDays);
        Assert.Equal(7, cached.FrequencyDays);
    }

    [Fact]
    public async Task EnvironmentVariablesOverrideTheSavedFieldOneByOne()
    {
        using var env = new BackupTestEnvironment(new()
        {
            ["Backup:Enabled"] = "false",
            ["Backup:FrequencyDays"] = "3",
            ["Backup:RetentionDays"] = "0", // not a valid number of days: ignored
        });
        await env.Settings.SaveAsync(new BackupSettings { Enabled = true, RetentionDays = 20, FrequencyDays = 1 });

        var effective = await env.Settings.GetEffectiveAsync();

        Assert.False(effective.Enabled);
        Assert.Equal(3, effective.FrequencyDays);
        Assert.Equal(20, effective.RetentionDays);
        Assert.True(env.Settings.IsSetByEnvironment(nameof(BackupSettings.Enabled)));
        Assert.True(env.Settings.IsSetByEnvironment(nameof(BackupSettings.FrequencyDays)));
        Assert.False(env.Settings.IsSetByEnvironment(nameof(BackupSettings.RetentionDays)));
        Assert.True((await env.Settings.GetStoredAsync()).Enabled);
    }

    [Fact]
    public async Task DaysAreCappedAtTheMaximum_FromTheEnvironmentAndFromSave()
    {
        using var env = new BackupTestEnvironment(new() { ["Backup:FrequencyDays"] = "99999999" });
        await env.Settings.SaveAsync(new BackupSettings { RetentionDays = 999_999, FrequencyDays = 1 });

        var effective = await env.Settings.GetEffectiveAsync();

        Assert.Equal(BackupSettings.MaxDays, effective.FrequencyDays);
        Assert.Equal(BackupSettings.MaxDays, effective.RetentionDays);
        // The capped values are ones a TimeSpan computed from now can hold.
        _ = DateTime.UtcNow - TimeSpan.FromDays(effective.RetentionDays);
        _ = DateTime.UtcNow + TimeSpan.FromDays(effective.FrequencyDays);
    }

    [Fact]
    public async Task SaveAsync_ClampsDaysToAtLeastOne()
    {
        using var env = new BackupTestEnvironment();

        await env.Settings.SaveAsync(new BackupSettings { RetentionDays = 0, FrequencyDays = -4 });

        var stored = await env.Settings.GetStoredAsync();
        Assert.Equal(1, stored.RetentionDays);
        Assert.Equal(1, stored.FrequencyDays);
    }
}
