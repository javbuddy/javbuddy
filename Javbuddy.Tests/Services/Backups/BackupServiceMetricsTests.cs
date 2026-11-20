using Javbuddy.Services.Backups;
using Javbuddy.Services.Metrics;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Tests.Services.Backups;

[Collection(SqlitePoolCollection.Name)]
public sealed class BackupServiceMetricsTests : IDisposable
{
    private readonly BackupTestEnvironment env = new();
    private readonly JavbuddyMetrics metrics;
    private readonly MetricsRecorder recorder;
    private readonly BackupService service;

    public BackupServiceMetricsTests()
    {
        metrics = new JavbuddyMetrics(env.Time);
        recorder = new MetricsRecorder(metrics);
        service = new BackupService(env.Location, env.Settings, env.Time, NullLogger<BackupService>.Instance, metrics);
    }

    public void Dispose()
    {
        recorder.Dispose();
        metrics.Dispose();
        env.Dispose();
    }

    private double? Age()
    {
        var before = recorder.Measurements.Count;
        recorder.SampleGauges();
        return recorder.Measurements.Skip(before).Where(m => m.InstrumentName == "javbuddy_last_backup_age_seconds")
            .Select(m => (double?)(double)m.Value!).SingleOrDefault();
    }

    [Fact]
    public async Task CreateAsync_UpdatesTheLastBackupAgeAtOnce()
    {
        Assert.Null(Age());

        await service.CreateAsync(BackupKind.Manual);
        env.Time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(600d, Age());
    }

    [Fact]
    public async Task DeleteAsync_OfTheNewestBackup_FallsBackToTheNextOneThenToNone()
    {
        var older = await service.CreateAsync(BackupKind.Manual);
        env.Time.Advance(TimeSpan.FromHours(1));
        var newer = await service.CreateAsync(BackupKind.Manual);
        Assert.Equal(0d, Age());

        await service.DeleteAsync(newer.FileName);
        Assert.Equal(3600d, Age());

        await service.DeleteAsync(older.FileName);
        Assert.Null(Age());
    }

    [Fact]
    public async Task ImportAsync_UpdatesTheLastBackupAge()
    {
        var source = await service.CreateAsync(BackupKind.Manual);
        var archive = File.ReadAllBytes(Path.Combine(env.Location.Folder, source.FileName));
        await service.DeleteAsync(source.FileName);
        Assert.Null(Age());

        env.Time.Advance(TimeSpan.FromMinutes(5));
        using var stream = new MemoryStream(archive);
        Assert.True((await service.ImportAsync(stream)).Success);

        // The import keeps the archive's own creation time, five minutes ago.
        Assert.Equal(300d, Age());
    }
}
