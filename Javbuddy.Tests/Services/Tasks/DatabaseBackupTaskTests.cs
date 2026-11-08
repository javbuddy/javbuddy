using Javbuddy.Models;
using Javbuddy.Services.Backups;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.Services.Backups;

namespace Javbuddy.Tests.Services.Tasks;

[Collection(Javbuddy.Tests.TestSupport.SqlitePoolCollection.Name)]
public sealed class DatabaseBackupTaskTests : IDisposable
{
    private readonly BackupTestEnvironment env = new();

    public void Dispose() => env.Dispose();

    private DatabaseBackupTask Task => new(env.Service, env.Settings);

    [Fact]
    public async Task RunAsync_CreatesAnAutomaticBackup()
    {
        var summary = await Task.RunAsync(CancellationToken.None, new Progress<TaskProgress>());

        var entry = Assert.Single(await env.Service.ListAsync());
        Assert.Equal(BackupKind.Auto, entry.Kind);
        Assert.Contains(entry.FileName, summary);
    }

    [Fact]
    public async Task RunAsync_WhenDisabled_DoesNotBackUp()
    {
        await env.Settings.SaveAsync(new BackupSettings { Enabled = false });

        var summary = await Task.RunAsync(CancellationToken.None, new Progress<TaskProgress>());

        Assert.Equal("automatic backups are off", summary);
        Assert.Empty(await env.Service.ListAsync());
    }

    [Fact]
    public void RunsImmediatelyWhenItHasNeverRun()
    {
        Assert.True(((IScheduledTask)Task).RunImmediatelyWhenNeverRun);
    }

    [Fact]
    public async Task GetInterval_FollowsTheFrequency_AndChecksDailyWhileOff()
    {
        await env.Settings.SaveAsync(new BackupSettings { Enabled = true, FrequencyDays = 3 });
        Assert.Equal(TimeSpan.FromDays(3), Task.GetInterval());

        await env.Settings.SaveAsync(new BackupSettings { Enabled = false, FrequencyDays = 3 });
        Assert.Equal(TimeSpan.FromDays(1), Task.GetInterval());
    }
}
