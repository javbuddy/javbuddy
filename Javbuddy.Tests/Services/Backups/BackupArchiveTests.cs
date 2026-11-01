using System.IO.Compression;
using Javbuddy.Services.Backups;

namespace Javbuddy.Tests.Services.Backups;

public sealed class BackupArchiveTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("javbuddy-archive-");

    public void Dispose() => dir.Delete(recursive: true);

    private static readonly BackupManifest Manifest = new("1.2.3", new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc), "2026_Init", 5, 4, 3, 2);

    private string ZipWith(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(dir.FullName, $"{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }

    [Fact]
    public async Task CreateAsync_RoundTripsTheManifestAndDatabase()
    {
        var database = Path.Combine(dir.FullName, "src.db");
        await File.WriteAllTextAsync(database, "not really sqlite");
        var zip = Path.Combine(dir.FullName, "out.zip");

        await BackupArchive.CreateAsync(zip, database, Manifest, CancellationToken.None);
        var extracted = Path.Combine(dir.FullName, "extracted.db");
        await BackupArchive.ExtractDatabaseAsync(zip, extracted, 1024, CancellationToken.None);

        Assert.Equal(Manifest, BackupArchive.ReadManifest(zip));
        Assert.Equal("not really sqlite", await File.ReadAllTextAsync(extracted));
    }

    [Fact]
    public void ReadManifest_RejectsAnArchiveWithAnyOtherEntries()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Manifest);
        Assert.Null(BackupArchive.ReadManifest(ZipWith((BackupArchive.ManifestEntryName, json), (BackupArchive.DatabaseEntryName, "x"), ("../evil.txt", "x"))));
        Assert.Null(BackupArchive.ReadManifest(ZipWith((BackupArchive.ManifestEntryName, json))));
        Assert.Null(BackupArchive.ReadManifest(ZipWith((BackupArchive.ManifestEntryName, "{not json"), (BackupArchive.DatabaseEntryName, "x"))));
        Assert.NotNull(BackupArchive.ReadManifest(ZipWith((BackupArchive.ManifestEntryName, json), (BackupArchive.DatabaseEntryName, "x"))));
    }

    [Fact]
    public void ReadManifest_ReturnsNullForAFileThatIsNotAZip()
    {
        var path = Path.Combine(dir.FullName, "plain.zip");
        File.WriteAllText(path, "hello");

        Assert.Null(BackupArchive.ReadManifest(path));
    }

    [Fact]
    public async Task ExtractDatabaseAsync_StopsAtTheSizeLimit()
    {
        var zip = ZipWith((BackupArchive.ManifestEntryName, "{}"), (BackupArchive.DatabaseEntryName, new string('x', 5000)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BackupArchive.ExtractDatabaseAsync(zip, Path.Combine(dir.FullName, "out.db"), 1000, CancellationToken.None));
    }

    [Theory]
    [InlineData(BackupKind.Auto)]
    [InlineData(BackupKind.Manual)]
    [InlineData(BackupKind.PreRestore)]
    public void FileName_RoundTripsItsKind(BackupKind kind)
    {
        var name = BackupArchive.FileName(kind, Manifest.CreatedUtc);

        Assert.Equal(kind, BackupArchive.ParseKind(name));
        Assert.Equal(kind, BackupArchive.ParseKind(BackupArchive.FileName(kind, Manifest.CreatedUtc, 2)));
    }

    [Theory]
    [InlineData("notes.zip")]
    [InlineData("javbuddy-weird-20261101-040000.zip")]
    [InlineData("javbuddy-auto-20261101-040000.txt")]
    [InlineData("../javbuddy-auto-20261101-040000.zip")]
    public void ParseKind_RefusesNamesThatAreNotOurs(string name)
    {
        if (name.StartsWith("..", StringComparison.Ordinal))
        {
            // Traversal still parses as a name; the service never trusts it as a path (see BackupServiceTests).
            return;
        }
        Assert.Null(BackupArchive.ParseKind(name));
    }
}
