using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Javbuddy.Services.Nfo;

public enum NfoWriteStatus
{
    Saved,
    FileChanged,
    ReadOnly,
    NotFound,
}

/// <summary>Outcome of an NfoFileWriter write. LastWriteUtc/Size are the written file's
/// fingerprint and PreviousContent is the text it replaced (for NfoHistoryService), all set only
/// when Status is Saved.</summary>
public sealed record NfoWriteResult(
    NfoWriteStatus Status,
    DateTime? LastWriteUtc = null,
    long? Size = null,
    string? PreviousContent = null);

/// <summary>The single path for every movie .nfo write. Writes the new content to a
/// sibling "{nfo}.tmp" file, then swaps it into place with File.Replace. On Unix File.Replace
/// rename()s the temp file over the .nfo, so the .nfo itself is never missing or half-written —
/// a media server scanning the folder mid-write sees either the old or the new file. If anything
/// fails, the temp file is removed and the original is left untouched. No backup file is left in
/// the media folder: the replaced content is returned instead, for the caller to keep as an
/// NfoGeneration. Unlike the in-place overwrite this replaced, it needs write access to the folder
/// (for the temp file), not just to the .nfo; the .nfo's Unix file mode is carried over where the
/// share allows chmod, its owner is not.
///
/// Only ever overwrites an existing, writable .nfo: a missing file (NotFound) or one with the
/// read-only attribute (ReadOnly) is refused without touching the folder.</summary>
public static class NfoFileWriter
{
    private const string TempSuffix = ".tmp";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Writes <paramref name="content"/> as UTF-8 (no BOM). When <paramref
    /// name="expectedContent"/> is given, the file is re-read first and nothing is written
    /// (FileChanged) unless it still matches exactly — the guard the conflict review uses so a
    /// save never clobbers an edit made after the review was opened. The check and the swap aren't
    /// one atomic step, but the file is re-read right before the swap, so an edit landing in that
    /// gap is still what PreviousContent returns.</summary>
    public static async Task<NfoWriteResult> WriteTextAsync(
        string nfoPath, string content, string? expectedContent, CancellationToken ct)
    {
        return await WriteAsync(nfoPath, expectedContent, async (stream, token) =>
        {
            await using var writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: true);
            await writer.WriteAsync(content.AsMemory(), token);
        }, ct);
    }

    /// <summary>Serializes <paramref name="doc"/> as-is — no re-indenting, so a document loaded
    /// with LoadOptions.PreserveWhitespace keeps the on-disk formatting it came with, and the XML
    /// declaration is written only if the document has one.</summary>
    public static async Task<NfoWriteResult> WriteXmlAsync(string nfoPath, XDocument doc, CancellationToken ct)
    {
        return await WriteAsync(nfoPath, expectedContent: null, async (stream, token) =>
        {
            var settings = new XmlWriterSettings
            {
                Async = true,
                Indent = false,
                OmitXmlDeclaration = doc.Declaration is null,
                Encoding = Utf8NoBom,
            };
            await using var writer = XmlWriter.Create(stream, settings);
            await doc.SaveAsync(writer, token);
        }, ct);
    }

    private static async Task<NfoWriteResult> WriteAsync(
        string nfoPath,
        string? expectedContent,
        Func<Stream, CancellationToken, Task> writeContent,
        CancellationToken ct)
    {
        var info = new FileInfo(nfoPath);
        if (!info.Exists) return new NfoWriteResult(NfoWriteStatus.NotFound);
        if (info.IsReadOnly) return new NfoWriteResult(NfoWriteStatus.ReadOnly);

        if (expectedContent is not null
            && !string.Equals(await ReadTextAsync(nfoPath, ct), expectedContent, StringComparison.Ordinal))
        {
            return new NfoWriteResult(NfoWriteStatus.FileChanged);
        }

        var tempPath = nfoPath + TempSuffix;
        string previousContent;
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await writeContent(stream, ct);
                await stream.FlushAsync(ct);
            }

            // The swapped-in file is the temp file's inode, so it would otherwise carry the
            // process umask's default mode instead of the .nfo's own (e.g. a group-writable
            // 0664 shared library file silently turning 0644). Windows' ReplaceFile already
            // carries the original's attributes/ACLs over itself.
            if (!OperatingSystem.IsWindows())
            {
                TryCopyUnixFileMode(nfoPath, tempPath);
            }

            // Read as late as possible, so the returned history is what the swap really replaced.
            previousContent = await ReadTextAsync(nfoPath, ct);
            File.Replace(tempPath, nfoPath, destinationBackupFileName: null);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        var written = new FileInfo(nfoPath);
        return new NfoWriteResult(NfoWriteStatus.Saved, written.LastWriteTimeUtc, written.Length, previousContent);
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken ct)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>Best effort: some NFS/ZFS shares with inherited ACLs refuse chmod (EPERM) even on a
    /// file the process just created, and there the temp file already carries the folder's
    /// inherited ACL — the right permissions — so a refused chmod must not fail the write.</summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void TryCopyUnixFileMode(string sourcePath, string targetPath)
    {
        try
        {
            var mode = File.GetUnixFileMode(sourcePath);
            if (File.GetUnixFileMode(targetPath) != mode)
            {
                File.SetUnixFileMode(targetPath, mode);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
