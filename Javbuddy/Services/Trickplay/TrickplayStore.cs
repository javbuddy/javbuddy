using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Trickplay;

/// <summary>Which video a trickplay set belongs to: the file's name, video duration (whole seconds)
/// and resolution, from its MediaInfo probe. A chapter write or video repair remuxes the file in
/// place, changing its size and modified time but none of these, so it keeps its trickplay; a real
/// replacement (another release, a re-encode) changes at least one of them.</summary>
public static class TrickplayIdentity
{
    private const int Length = 16;

    /// <summary>Null when the file hasn't been probed for all of them yet.</summary>
    public static string? For(string fileName, double? durationSeconds, int? width, int? height)
    {
        if (string.IsNullOrWhiteSpace(fileName) || durationSeconds is not > 0 || width is not > 0 || height is not > 0) return null;

        var key = string.Create(CultureInfo.InvariantCulture,
            $"{fileName.ToLowerInvariant()}|{Math.Round(durationSeconds.Value)}|{width}x{height}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..Length];
    }

    /// <summary>A highlight's own set: the clip [startMs, endMs] of the file with
    /// <paramref name="fileIdentity"/>, one thumbnail every <paramref name="intervalMs"/>. Moving the
    /// highlight or replacing the file changes it, so the old set just stops being used.</summary>
    public static string ForClip(string fileIdentity, long startMs, long endMs, int intervalMs)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"{fileIdentity}|clip|{startMs}-{endMs}|{intervalMs}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..Length];
    }

    public static bool IsValid(string? identity) =>
        identity is { Length: Length } && identity.All(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c));
}

/// <summary>Durable storage of generated trickplay: tile sheets in the durable
/// object store under <c>trickplay/&lt;CODE&gt;/&lt;identity&gt;/0.webp, 1.webp, …</c>, indexed by
/// <see cref="TrickplaySet"/> rows — durable data like actor photos, not the disposable image
/// cache.</summary>
public interface ITrickplayStore
{
    /// <summary>The set's row, or null when the identity is malformed or no set was saved for it.</summary>
    Task<TrickplaySet?> GetSetAsync(string code, string identity, CancellationToken ct = default);

    /// <summary>One tile sheet of a set, or null when the identity is malformed or the sheet
    /// doesn't exist. The caller disposes the result.</summary>
    Task<StoredObject?> OpenSheetAsync(string code, string identity, int index, CancellationToken ct = default);

    /// <summary>Saves a set for code: writes its sheets (in index order), then its row — the commit
    /// marker, since the store can't write several objects atomically — replacing any existing set
    /// with the same identity.</summary>
    Task SaveAsync(string code, TrickplaySet set, IReadOnlyList<Func<Stream>> sheets, CancellationToken ct = default);

    /// <summary>Every saved set, by its code folder and identity.</summary>
    Task<IReadOnlyList<TrickplaySetLocation>> ListSetsAsync(CancellationToken ct = default);

    /// <summary>Deletes the row, then the sheets.</summary>
    Task DeleteSetAsync(TrickplaySetLocation set, CancellationToken ct = default);

    /// <summary>Deletes stored sheets that belong to no saved set (a generation interrupted by a crash
    /// or restart, or a lost database) once nothing under their set's prefix is newer than
    /// olderThan; returns how many sets' worth.</summary>
    Task<int> DeleteOrphanedSheetsAsync(TimeSpan olderThan, CancellationToken ct = default);
}

/// <summary>Where one stored set lives: its code folder (see TrickplayStore.CodeFolder) and identity;
/// IsClip for a highlight's set.</summary>
public sealed record TrickplaySetLocation(string CodeFolder, string Identity, bool IsClip = false);

public sealed class TrickplayStore(IDbContextFactory<AppDbContext> dbFactory, IObjectStoreProvider stores) : ITrickplayStore
{
    private readonly IObjectStore store = stores.Get(ObjectStoreArea.Trickplay);

    /// <summary>The code made safe as a key segment, since codes may contain characters a folder
    /// name can't.</summary>
    public static string CodeFolder(string code)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(code.Trim().ToUpperInvariant().Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray());
    }

    public static string SheetName(int index) => index.ToString(CultureInfo.InvariantCulture) + ".webp";

    private static string Prefix(string codeFolder, string identity) => $"{codeFolder}/{identity}/";

    private static bool IsValidCodeFolder(string codeFolder) =>
        codeFolder is not ("" or "." or "..") && codeFolder == CodeFolder(codeFolder);

    public async Task<TrickplaySet?> GetSetAsync(string code, string identity, CancellationToken ct = default)
    {
        if (!TrickplayIdentity.IsValid(identity) || string.IsNullOrWhiteSpace(code)) return null;

        var codeFolder = CodeFolder(code);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TrickplaySets.AsNoTracking().FirstOrDefaultAsync(set => set.CodeFolder == codeFolder && set.Identity == identity, ct);
    }

    public async Task<StoredObject?> OpenSheetAsync(string code, string identity, int index, CancellationToken ct = default)
    {
        if (!TrickplayIdentity.IsValid(identity) || string.IsNullOrWhiteSpace(code) || index < 0) return null;

        var codeFolder = CodeFolder(code);
        return IsValidCodeFolder(codeFolder) ? await store.OpenReadAsync(Prefix(codeFolder, identity) + SheetName(index), ct) : null;
    }

    public async Task SaveAsync(string code, TrickplaySet set, IReadOnlyList<Func<Stream>> sheets, CancellationToken ct = default)
    {
        if (!TrickplayIdentity.IsValid(set.Identity)) throw new ArgumentException("Malformed trickplay identity.", nameof(set));
        var codeFolder = CodeFolder(code);
        if (!IsValidCodeFolder(codeFolder)) throw new ArgumentException("The code can't be used as a trickplay folder.", nameof(code));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Uncommit any existing set first, so nothing reads it while its sheets are being replaced.
        await db.TrickplaySets.Where(s => s.CodeFolder == codeFolder && s.Identity == set.Identity).ExecuteDeleteAsync(ct);

        var prefix = Prefix(codeFolder, set.Identity);
        await store.DeletePrefixAsync(prefix, ct);
        for (var index = 0; index < sheets.Count; index++)
        {
            await using var sheet = sheets[index]();
            await store.WriteAsync(prefix + SheetName(index), sheet, ct);
        }

        set.Id = 0;
        set.CodeFolder = codeFolder;
        db.TrickplaySets.Add(set);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TrickplaySetLocation>> ListSetsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TrickplaySets.AsNoTracking()
            .Select(set => new TrickplaySetLocation(set.CodeFolder, set.Identity, set.SourceIdentity != null))
            .ToListAsync(ct);
    }

    public async Task DeleteSetAsync(TrickplaySetLocation set, CancellationToken ct = default)
    {
        if (!TrickplayIdentity.IsValid(set.Identity) || !IsValidCodeFolder(set.CodeFolder)) return;

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await db.TrickplaySets.Where(s => s.CodeFolder == set.CodeFolder && s.Identity == set.Identity).ExecuteDeleteAsync(ct);
        }
        await store.DeletePrefixAsync(Prefix(set.CodeFolder, set.Identity), ct);
    }

    public async Task<int> DeleteOrphanedSheetsAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        // Listed before the rows are read: a set committed in between then still counts as saved.
        var newestByPrefix = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        await foreach (var item in store.ListAsync("", ct))
        {
            var segments = item.Key.Split('/');
            if (segments.Length < 3) continue;

            var prefix = Prefix(segments[0], segments[1]);
            if (!newestByPrefix.TryGetValue(prefix, out var newest) || item.LastModified > newest)
            {
                newestByPrefix[prefix] = item.LastModified;
            }
        }
        if (newestByPrefix.Count == 0) return 0;

        HashSet<string> saved;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            saved = (await db.TrickplaySets.AsNoTracking().Select(set => new { set.CodeFolder, set.Identity }).ToListAsync(ct))
                .Select(set => Prefix(set.CodeFolder, set.Identity))
                .ToHashSet(StringComparer.Ordinal);
        }

        var cutoff = DateTimeOffset.UtcNow - olderThan;
        var deleted = 0;
        foreach (var (prefix, newest) in newestByPrefix)
        {
            if (saved.Contains(prefix) || newest >= cutoff) continue;
            try
            {
                await store.DeletePrefixAsync(prefix, ct);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return deleted;
    }
}
