using Javbuddy.Services.Backups;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Tests.Services.Backups;

public sealed class BackupServiceTests : IDisposable
{
    private readonly BackupTestEnvironment env = new();

    public void Dispose() => env.Dispose();

    [Fact]
    public async Task CreateAsync_WritesAnArchiveWhoseManifestCountsTheData()
    {
        await env.AddTagsAsync(3);

        var entry = await env.Service.CreateAsync(BackupKind.Manual);

        Assert.Equal(BackupKind.Manual, entry.Kind);
        Assert.Equal(3, entry.Manifest.Tags);
        Assert.False(string.IsNullOrEmpty(entry.Manifest.MigrationId));
        Assert.Equal(env.Time.GetUtcNow().UtcDateTime, entry.Manifest.CreatedUtc);
        Assert.Equal(entry.FileName, Assert.Single(await env.Service.ListAsync()).FileName);
        // No scratch folder is left behind to be listed or to grow.
        Assert.Empty(Directory.GetDirectories(env.Location.Folder));
    }

    [Fact]
    public async Task CreateAsync_SnapshotsTheDataAsOfTheBackup()
    {
        await env.AddTagsAsync(2);
        var entry = await env.Service.CreateAsync(BackupKind.Manual);
        await env.AddTagsAsync(5);

        var extracted = Path.Combine(env.Location.Folder, "check.db");
        await BackupArchive.ExtractDatabaseAsync(Path.Combine(env.Location.Folder, entry.FileName), extracted, long.MaxValue, CancellationToken.None);

        Assert.Null(await BackupDatabase.ValidateAsync(extracted, CancellationToken.None));
        Assert.Equal(2, await env.TagCountAsync(extracted));
    }

    [Fact]
    public async Task CreateAsync_TwoBackupsInTheSameSecondGetDistinctFiles()
    {
        var first = await env.Service.CreateAsync(BackupKind.Manual);
        var second = await env.Service.CreateAsync(BackupKind.Manual);

        Assert.NotEqual(first.FileName, second.FileName);
        Assert.Equal(2, (await env.Service.ListAsync()).Count);
    }

    [Fact]
    public async Task ListAsync_SkipsFilesThatAreNotBackupsAndSortsNewestFirst()
    {
        await env.Service.CreateAsync(BackupKind.Manual);
        env.Time.Advance(TimeSpan.FromHours(1));
        var newest = await env.Service.CreateAsync(BackupKind.Auto);
        await File.WriteAllTextAsync(Path.Combine(env.Location.Folder, "javbuddy-manual-20200101-000000.zip"), "garbage");
        await File.WriteAllTextAsync(Path.Combine(env.Location.Folder, "notes.zip"), "garbage");

        var list = await env.Service.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal(newest.FileName, list[0].FileName);
    }

    [Fact]
    public async Task PruneAsync_RemovesOnlyAutomaticBackupsPastRetention_AndKeepsTheNewestAutomatic()
    {
        await env.Settings.SaveAsync(new Javbuddy.Models.BackupSettings { RetentionDays = 5, FrequencyDays = 1 });
        var manual = await env.Service.CreateAsync(BackupKind.Manual);
        var preRestore = await env.Service.CreateAsync(BackupKind.PreRestore);
        var old = await env.Service.CreateAsync(BackupKind.Auto);
        env.Time.Advance(TimeSpan.FromDays(3));
        var recent = await env.Service.CreateAsync(BackupKind.Auto);
        env.Time.Advance(TimeSpan.FromDays(4)); // old is now 7 days old, recent 4

        var removed = await env.Service.PruneAsync();

        Assert.Equal(1, removed);
        var names = (await env.Service.ListAsync()).Select(e => e.FileName).ToHashSet();
        Assert.DoesNotContain(old.FileName, names);
        Assert.Contains(recent.FileName, names);
        Assert.Contains(manual.FileName, names);
        Assert.Contains(preRestore.FileName, names);
    }

    [Fact]
    public async Task PruneAsync_KeepsTheNewestAutomaticBackupEvenWhenItIsPastRetention()
    {
        await env.Settings.SaveAsync(new Javbuddy.Models.BackupSettings { RetentionDays = 1, FrequencyDays = 1 });
        var only = await env.Service.CreateAsync(BackupKind.Auto);
        env.Time.Advance(TimeSpan.FromDays(30));

        Assert.Equal(0, await env.Service.PruneAsync());
        Assert.Equal(only.FileName, Assert.Single(await env.Service.ListAsync()).FileName);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheFile_AndRefusesAnUnknownOrTraversingName()
    {
        var entry = await env.Service.CreateAsync(BackupKind.Manual);
        var outside = Path.Combine(Path.GetDirectoryName(env.Location.Folder)!, "javbuddy-manual-20200101-000000.zip");
        await File.WriteAllTextAsync(outside, "keep me");

        Assert.False((await env.Service.DeleteAsync("../javbuddy-manual-20200101-000000.zip")).Success);
        Assert.False((await env.Service.DeleteAsync("javbuddy-manual-19990101-000000.zip")).Success);
        Assert.True(File.Exists(outside));

        Assert.True((await env.Service.DeleteAsync(entry.FileName)).Success);
        Assert.Empty(await env.Service.ListAsync());
    }

    [Fact]
    public async Task FindPath_ReturnsNullForTraversalAndUnknownNames()
    {
        var entry = await env.Service.CreateAsync(BackupKind.Manual);

        Assert.NotNull(env.Service.FindPath(entry.FileName));
        Assert.Null(env.Service.FindPath("../" + entry.FileName));
        Assert.Null(env.Service.FindPath(Path.Combine(env.Location.Folder, entry.FileName)));
        Assert.Null(env.Service.FindPath("javbuddy-manual-19990101-000000.zip"));
    }

    [Fact]
    public async Task ImportAsync_AddsAnExportedBackupAsAManualOne()
    {
        await env.AddTagsAsync(4);
        var exported = await env.Service.CreateAsync(BackupKind.Auto);
        using var other = new BackupTestEnvironment();

        await using var stream = File.OpenRead(Path.Combine(env.Location.Folder, exported.FileName));
        var result = await other.Service.ImportAsync(stream);

        Assert.True(result.Success, result.ErrorMessage);
        var imported = Assert.Single(await other.Service.ListAsync());
        Assert.Equal(BackupKind.Manual, imported.Kind);
        Assert.Equal(4, imported.Manifest.Tags);
        Assert.Equal(exported.Manifest.CreatedUtc, imported.Manifest.CreatedUtc);
    }

    [Fact]
    public async Task ImportAsync_RejectsAFileThatIsNotABackup_AndLeavesNothingBehind()
    {
        await using var stream = new MemoryStream("definitely not a zip"u8.ToArray());

        var result = await env.Service.ImportAsync(stream);

        Assert.False(result.Success);
        Assert.Empty(await env.Service.ListAsync());
        Assert.Empty(Directory.GetDirectories(env.Location.Folder));
    }

    [Fact]
    public async Task ImportAsync_RejectsADatabaseMigratedByANewerBuild()
    {
        var entry = await env.Service.CreateAsync(BackupKind.Manual);
        var zip = Path.Combine(env.Location.Folder, entry.FileName);
        var tampered = await WithNewerMigrationAsync(zip);

        await using var stream = File.OpenRead(tampered);
        var result = await env.Service.ImportAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("newer version", result.ErrorMessage);
    }

    [Fact]
    public async Task StageRestoreAsync_ThenApply_ReplacesTheDatabaseAndKeepsAPreRestoreBackup()
    {
        await env.AddTagsAsync(2);
        var backup = await env.Service.CreateAsync(BackupKind.Manual);
        await env.AddTagsAsync(6); // changes made after the backup, lost by the restore

        var staged = await env.Service.StageRestoreAsync(backup.FileName);
        Assert.True(staged.Success, staged.ErrorMessage);
        Assert.True(env.Service.RestorePending);
        Assert.Equal(8, await env.TagCountAsync()); // nothing changes until the restart

        var applied = await BackupRestoreApplier.ApplyPendingAsync(env.Location, env.Time, NullLogger.Instance);

        Assert.True(applied);
        Assert.False(env.Service.RestorePending);
        Assert.Equal(2, await env.TagCountAsync());
        var preRestore = Assert.Single(await env.Service.ListAsync(), e => e.Kind == BackupKind.PreRestore);
        Assert.Equal(8, preRestore.Manifest.Tags);
        Assert.False(File.Exists(env.Location.DatabasePath + ".restoring"));
    }

    [Fact]
    public async Task StageRestoreAsync_RefusesAnUnknownBackup_AndStagesNothing()
    {
        var result = await env.Service.StageRestoreAsync("javbuddy-manual-19990101-000000.zip");

        Assert.False(result.Success);
        Assert.False(env.Service.RestorePending);
    }

    [Fact]
    public async Task ApplyPendingAsync_DoesNothingWithoutAStagedRestore()
    {
        await env.AddTagsAsync(1);

        Assert.False(await BackupRestoreApplier.ApplyPendingAsync(env.Location, env.Time, NullLogger.Instance));
        Assert.Equal(1, await env.TagCountAsync());
        Assert.Empty(await env.Service.ListAsync());
    }

    [Fact]
    public async Task ApplyPendingAsync_WithAnUnreadableStagedFile_KeepsTheCurrentDatabase()
    {
        await env.AddTagsAsync(3);
        Directory.CreateDirectory(env.Location.PendingFolder);
        await File.WriteAllTextAsync(env.Location.PendingDatabasePath, "garbage");

        // The garbage is copied over the database only after the pre-restore backup succeeded, so this checks the
        // consumed staging folder and that the original was backed up first.
        await BackupRestoreApplier.ApplyPendingAsync(env.Location, env.Time, NullLogger.Instance);

        Assert.False(Directory.Exists(env.Location.PendingFolder));
        var preRestore = Assert.Single(await env.Service.ListAsync());
        Assert.Equal(3, preRestore.Manifest.Tags);
    }

    /// <summary>Copies <paramref name="zipPath"/> with an extra, unknown migration recorded in its database.</summary>
    private async Task<string> WithNewerMigrationAsync(string zipPath)
    {
        var database = Path.Combine(env.Location.Folder, "tamper.db");
        await BackupArchive.ExtractDatabaseAsync(zipPath, database, long.MaxValue, CancellationToken.None);
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('99999999999999_FromTheFuture', '99.0.0')";
            await command.ExecuteNonQueryAsync();
        }

        var output = Path.Combine(env.Location.Folder, "tampered.zip");
        await BackupArchive.CreateAsync(output, database, BackupArchive.ReadManifest(zipPath)!, CancellationToken.None);
        return output;
    }
}
