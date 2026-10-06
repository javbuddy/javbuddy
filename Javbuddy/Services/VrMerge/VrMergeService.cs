using Javbuddy.Services.Ffmpeg;

namespace Javbuddy.Services.VrMerge;

public interface IVrMergeService
{
    /// <summary>Scans a folder for VR release parts (see VrPartDetector.DetectParts) and probes each
    /// survivor for duration/stream shape. Returns null if fewer than 2 parts are detected.</summary>
    Task<VrMergeCandidate?> DetectAsync(string folderPath, CancellationToken ct = default);

    /// <summary>Merges the given parts (already ordered/filtered by the caller) into
    /// "&lt;folder&gt;.mp4", via a "&lt;folder&gt;.merging.mp4" staging name so a failed or
    /// interrupted run never leaves a plausible-looking file for javinizer-go to pick up. Source
    /// parts are left untouched.</summary>
    Task<VrMergeResult> MergeAsync(VrMergeRequest request, IProgress<FfmpegProgress>? progress, CancellationToken ct = default);
}

public class VrMergeService(IFfmpegClient ffmpegClient) : IVrMergeService
{
    public async Task<VrMergeCandidate?> DetectAsync(string folderPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(folderPath)) return null;

        var files = Directory.GetFiles(folderPath)
            .Select(p => new VrFileEntry(p, Path.GetFileName(p), new FileInfo(p).Length))
            .ToList();

        var detected = VrPartDetector.DetectParts(files);
        if (detected.Count < 2) return null;

        var codeBase = Path.GetFileName(folderPath.TrimEnd('/', '\\')).ToUpperInvariant();
        var parts = new List<VrMergeCandidatePart>();
        var warnings = new List<string>();
        FfprobeMediaInfo? reference = null;

        for (var i = 0; i < detected.Count; i++)
        {
            var file = detected[i];
            var chapterTitle = VrPartDetector.ChapterTitle(codeBase, i);
            var info = await ffmpegClient.ProbeAsync(file.Path, ct);

            if (info is null)
            {
                warnings.Add($"Could not read \"{file.Name}\" with ffprobe — check it's a valid video file.");
                parts.Add(new VrMergeCandidatePart
                {
                    Path = file.Path,
                    Name = file.Name,
                    SizeBytes = file.SizeBytes,
                    DurationSeconds = 0,
                    ChapterTitle = chapterTitle,
                });
                continue;
            }

            reference ??= info;
            if (info.VideoCodec != reference.VideoCodec || info.Width != reference.Width || info.Height != reference.Height)
            {
                warnings.Add($"\"{file.Name}\" has a different video codec or resolution than the first part — stream copy may produce a broken file.");
            }
            if (info.AudioCodec != reference.AudioCodec || info.AudioChannels != reference.AudioChannels)
            {
                warnings.Add($"\"{file.Name}\" has a different audio codec or channel count than the first part.");
            }

            parts.Add(new VrMergeCandidatePart
            {
                Path = file.Path,
                Name = file.Name,
                SizeBytes = file.SizeBytes,
                DurationSeconds = info.DurationSeconds,
                VideoCodec = info.VideoCodec,
                Width = info.Width,
                Height = info.Height,
                FrameRate = info.FrameRate,
                AudioCodec = info.AudioCodec,
                AudioChannels = info.AudioChannels,
                ChapterTitle = chapterTitle,
            });
        }

        return new VrMergeCandidate
        {
            FolderPath = folderPath,
            CodeBase = codeBase,
            Parts = parts,
            Warnings = warnings,
        };
    }

    public async Task<VrMergeResult> MergeAsync(VrMergeRequest request, IProgress<FfmpegProgress>? progress, CancellationToken ct = default)
    {
        if (request.OrderedParts.Count < 2)
        {
            return VrMergeResult.Failed("At least two parts are required to merge.");
        }

        foreach (var part in request.OrderedParts)
        {
            if (!File.Exists(part.Path))
            {
                return VrMergeResult.Failed($"\"{part.Name}\" no longer exists — it may have been moved or deleted since detection.");
            }
        }

        var folderName = Path.GetFileName(request.FolderPath.TrimEnd('/', '\\'));
        var outputPath = Path.Combine(request.FolderPath, folderName + ".mp4");
        if (File.Exists(outputPath))
        {
            return VrMergeResult.Failed($"\"{Path.GetFileName(outputPath)}\" already exists in this folder — remove or rename it before merging.");
        }

        var mergingPath = Path.Combine(request.FolderPath, folderName + ".merging.mp4");

        var freeSpaceError = CheckFreeSpace(request.FolderPath, request.OrderedParts.Sum(p => p.SizeBytes));
        if (freeSpaceError is not null)
        {
            return VrMergeResult.Failed(freeSpaceError);
        }

        var concatParts = request.OrderedParts
            .Select((p, i) => new FfmpegConcatPart(
                p.Path,
                VrPartDetector.ChapterTitle(request.CodeBase, i),
                (long)Math.Round(p.DurationSeconds * 1_000_000)))
            .ToList();

        var runResult = await ffmpegClient.ConcatWithChaptersAsync(new FfmpegConcatRequest(concatParts, mergingPath), progress, ct);
        if (!runResult.Success)
        {
            TryDeletePartial(mergingPath);
            return VrMergeResult.Failed(runResult.ErrorMessage ?? "ffmpeg failed to merge the parts.");
        }

        if (!File.Exists(mergingPath) || new FileInfo(mergingPath).Length == 0)
        {
            TryDeletePartial(mergingPath);
            return VrMergeResult.Failed("ffmpeg reported success but produced no output file.");
        }

        var verifyInfo = await ffmpegClient.ProbeAsync(mergingPath, ct);
        if (verifyInfo is null || verifyInfo.ChapterCount != request.OrderedParts.Count)
        {
            var actual = verifyInfo?.ChapterCount.ToString() ?? "unknown";
            TryDeletePartial(mergingPath);
            return VrMergeResult.Failed($"Merged file failed verification (expected {request.OrderedParts.Count} chapters, found {actual}).");
        }

        File.Move(mergingPath, outputPath);
        return VrMergeResult.Ok(outputPath);
    }

    /// <summary>Best-effort free-space preflight (5% headroom over the summed part size). Skipped
    /// silently if the folder's drive can't be resolved (e.g. a UNC path on this platform) rather
    /// than blocking the merge on an unrelated hard-to-anticipate case.</summary>
    private static string? CheckFreeSpace(string folderPath, long totalPartBytes)
    {
        try
        {
            var root = Path.GetPathRoot(folderPath);
            if (string.IsNullOrEmpty(root)) return null;

            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < totalPartBytes * 1.05)
            {
                return "Not enough free disk space to merge these parts (need the combined part size plus ~5%).";
            }
        }
        catch (ArgumentException)
        {
        }

        return null;
    }

    private static void TryDeletePartial(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
