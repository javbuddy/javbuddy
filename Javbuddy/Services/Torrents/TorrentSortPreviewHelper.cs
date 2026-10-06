using Javbuddy.Services.Javinizer;

namespace Javbuddy.Services.Torrents;

/// <summary>One file the Organize step will create on disk, for the Preview step's file-tree
/// display. RelativePath is just the file's own name (e.g. "START-591.mp4" or, for a nested
/// entry, "fanart1.jpg") — never an absolute path and never prefixed with its parent folder, so
/// the UI can render the "extrafanart/" folder line once and list nested entries under it.</summary>
public record PreviewTreeEntry(string RelativePath, bool IsNested);

/// <summary>Turns javinizer-go's OrganizePreviewResponseDto into a flat list of the files it will
/// actually write, for TorrentSort.razor's Preview step. Field semantics verified against
/// javinizer-go's own source (internal/api/contracts/batch_types.go,
/// internal/workflow/preview_orchestrator.go, internal/downloader/media_path_resolver.go):
/// VideoFiles/NFOPaths/PosterPath/FanartPath/TrailerPath are already full target paths sharing the
/// movie's folder; ExtrafanartPath is that folder's extrafanart *subfolder* path, and Screenshots
/// are bare filenames meant to be joined under it — not full paths themselves.</summary>
public static class TorrentSortPreviewHelper
{
    public static List<PreviewTreeEntry> BuildFileTree(OrganizePreviewResponseDto preview)
    {
        var entries = new List<PreviewTreeEntry>();

        var videoFiles = preview.VideoFiles is { Count: > 0 } ? preview.VideoFiles : NonBlank(preview.FullPath);
        foreach (var path in videoFiles)
        {
            AddFile(entries, path);
        }

        var nfoFiles = preview.NfoPaths is { Count: > 0 } ? preview.NfoPaths : NonBlank(preview.NfoPath);
        foreach (var path in nfoFiles)
        {
            AddFile(entries, path);
        }

        AddFile(entries, preview.PosterPath);
        AddFile(entries, preview.FanartPath);
        AddFile(entries, preview.TrailerPath);

        if (!string.IsNullOrWhiteSpace(preview.ExtraFanartPath) && preview.Screenshots is { Count: > 0 })
        {
            foreach (var name in preview.Screenshots)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                entries.Add(new PreviewTreeEntry(name, IsNested: true));
            }
        }

        return entries;
    }

    public static string GetFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Unknown file";
        var idx = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return idx >= 0 && idx < path.Length - 1 ? path[(idx + 1)..] : path;
    }

    private static void AddFile(List<PreviewTreeEntry> entries, string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return;
        entries.Add(new PreviewTreeEntry(GetFileName(fullPath), IsNested: false));
    }

    private static IEnumerable<string> NonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : [value];
}
