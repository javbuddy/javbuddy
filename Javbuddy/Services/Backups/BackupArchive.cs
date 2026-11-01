using System.IO.Compression;
using System.Text.Json;

namespace Javbuddy.Services.Backups;

/// <summary>The backup zip format: exactly two entries, the database snapshot and a <c>backup.json</c> manifest.
/// Archives come from users (Import), so reading is strict: only those two names are accepted, and extraction
/// counts the bytes it really writes rather than trusting the size the archive declares.</summary>
public static class BackupArchive
{
    public const string DatabaseEntryName = "Javbuddy.db";
    public const string ManifestEntryName = "backup.json";

    private const string FilePrefix = "javbuddy-";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>A fresh scratch folder inside <paramref name="parent"/> (the backup folder, so the finished file moves
    /// into place on the same volume). Hidden from the backup list by its leading dot.</summary>
    public static DirectoryInfo CreateWorkFolder(string parent) =>
        Directory.CreateDirectory(Path.Combine(parent, ".tmp-" + Guid.NewGuid().ToString("N")));

    public static string FileName(BackupKind kind, DateTime utc, int duplicate = 0) =>
        $"{FilePrefix}{KindName(kind)}-{utc:yyyyMMdd-HHmmss}{(duplicate > 0 ? $"-{duplicate}" : "")}.zip";

    public static string KindName(BackupKind kind) => kind switch
    {
        BackupKind.Auto => "auto",
        BackupKind.PreRestore => "pre-restore",
        _ => "manual",
    };

    /// <summary>The kind encoded in a backup's file name, or null when the name isn't one of ours.</summary>
    public static BackupKind? ParseKind(string fileName)
    {
        if (!fileName.StartsWith(FilePrefix, StringComparison.Ordinal) || !fileName.EndsWith(".zip", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var kind in Enum.GetValues<BackupKind>())
        {
            if (fileName.StartsWith($"{FilePrefix}{KindName(kind)}-", StringComparison.Ordinal))
            {
                return kind;
            }
        }
        return null;
    }

    public static async Task CreateAsync(string zipPath, string databasePath, BackupManifest manifest, CancellationToken ct)
    {
        await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);

        var database = zip.CreateEntry(DatabaseEntryName, CompressionLevel.Optimal);
        await using (var entry = database.Open())
        await using (var source = File.OpenRead(databasePath))
        {
            await source.CopyToAsync(entry, ct);
        }

        var manifestEntry = zip.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        await using var manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, ct);
    }

    /// <summary>The manifest of a valid backup archive, or null when the file isn't one (not a zip, other or
    /// missing entries, unreadable manifest).</summary>
    public static BackupManifest? ReadManifest(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (!HasExpectedEntries(zip))
            {
                return null;
            }

            using var stream = zip.GetEntry(ManifestEntryName)!.Open();
            var manifest = JsonSerializer.Deserialize<BackupManifest>(stream);
            return manifest is { AppVersion: not null, MigrationId: not null } ? manifest : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Writes the archive's database to <paramref name="destinationPath"/>. Throws
    /// <see cref="InvalidDataException"/> when the archive isn't a backup or the database exceeds
    /// <paramref name="maxBytes"/>.</summary>
    public static async Task ExtractDatabaseAsync(string zipPath, string destinationPath, long maxBytes, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (!HasExpectedEntries(zip))
        {
            throw new InvalidDataException("The file is not a Javbuddy backup.");
        }

        await using var source = zip.GetEntry(DatabaseEntryName)!.Open();
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("The backup's database is larger than allowed.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static bool HasExpectedEntries(ZipArchive zip) =>
        zip.Entries.Count == 2
        && zip.GetEntry(DatabaseEntryName) is not null
        && zip.GetEntry(ManifestEntryName) is not null;
}
