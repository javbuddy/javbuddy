using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.Movies;

namespace Javbuddy.Services.VideoRepair;

/// <summary>Replaces a video file with an ffmpeg-produced copy of itself, safely: free-space check,
/// write to a staging file next to the original (named with MovieVersionParser.RepairStagingMarker so
/// library scans ignore it), probe-verify the result, then rotate original → .bak, staging →
/// original, delete .bak — restoring the original if anything fails or is cancelled. Shared by the
/// lossless B-frame repair and writing scene chapters.</summary>
public static class VideoFileReplacer
{
    /// <param name="produce">Writes the new file to the given staging path.</param>
    /// <param name="verify">Checks the probed staging file; returns an error message, or null when it's good.</param>
    public static async Task<VideoRepairResult> ReplaceAsync(
        string targetPath,
        IFfmpegClient ffmpegClient,
        Func<string, Task<FfmpegRunResult>> produce,
        Func<FfprobeMediaInfo, string?> verify,
        ILogger? logger,
        CancellationToken ct)
    {
        if (!File.Exists(targetPath))
        {
            return VideoRepairResult.Failed($"Target file \"{Path.GetFileName(targetPath)}\" not found at {targetPath}.");
        }

        var dir = Path.GetDirectoryName(targetPath) ?? ".";
        var freeSpaceError = CheckFreeSpace(dir, new FileInfo(targetPath).Length);
        if (freeSpaceError is not null)
        {
            return VideoRepairResult.Failed(freeSpaceError);
        }

        var nameWithoutExt = Path.GetFileNameWithoutExtension(targetPath);
        var ext = Path.GetExtension(targetPath);
        var stagingPath = Path.Combine(dir, $"{nameWithoutExt}{MovieVersionParser.RepairStagingMarker}{Guid.NewGuid():N}{ext}");
        var bakPath = targetPath + ".bak";

        try
        {
            logger?.LogInformation("Writing ffmpeg output for {TargetPath} to {StagingPath}", targetPath, stagingPath);
            var produced = await produce(stagingPath);
            if (!produced.Success)
            {
                TryDelete(stagingPath);
                return VideoRepairResult.Failed(produced.ErrorMessage ?? "FFmpeg process failed.");
            }

            if (!File.Exists(stagingPath) || new FileInfo(stagingPath).Length == 0)
            {
                TryDelete(stagingPath);
                return VideoRepairResult.Failed("FFmpeg reported success but produced an empty output file.");
            }

            var verifyProbe = await ffmpegClient.ProbeAsync(stagingPath, ct);
            var verifyError = verifyProbe is null || verifyProbe.DurationSeconds <= 0
                ? "The new video file failed its verification probe."
                : verify(verifyProbe);
            if (verifyError is not null)
            {
                TryDelete(stagingPath);
                return VideoRepairResult.Failed(verifyError);
            }

            // Atomic rotation
            TryDelete(bakPath);
            File.Move(targetPath, bakPath);
            File.Move(stagingPath, targetPath);
            TryDelete(bakPath);

            logger?.LogInformation("Replaced {TargetPath} with the new file", targetPath);
            return VideoRepairResult.Ok(targetPath);
        }
        catch (OperationCanceledException)
        {
            TryDelete(stagingPath);
            RestoreBackup(targetPath, bakPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(stagingPath);
            RestoreBackup(targetPath, bakPath);
            logger?.LogError(ex, "Unexpected error while replacing {TargetPath}", targetPath);
            return VideoRepairResult.Failed($"Replacing the video file failed: {ex.Message}");
        }
    }

    private static string? CheckFreeSpace(string folderPath, long fileBytes)
    {
        try
        {
            var root = Path.GetPathRoot(folderPath);
            if (string.IsNullOrEmpty(root)) return null;

            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < fileBytes * 1.05)
            {
                return "Not enough free disk space to rewrite this file (requires file size plus ~5% headroom).";
            }
        }
        catch (ArgumentException)
        {
        }

        return null;
    }

    private static void RestoreBackup(string targetPath, string bakPath)
    {
        if (!File.Exists(targetPath) && File.Exists(bakPath))
        {
            File.Move(bakPath, targetPath);
        }
    }

    private static void TryDelete(string path)
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
