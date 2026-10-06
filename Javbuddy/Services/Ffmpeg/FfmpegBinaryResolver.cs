namespace Javbuddy.Services.Ffmpeg;

/// <summary>Resolves the ffmpeg/ffprobe executable paths once and caches the result. No NuGet
/// package ships a current ffmpeg CLI build for both Windows and Linux (checked directly against
/// nuget.org — the only cross-platform option, Curiosity.FFmpeg.Runtimes, was stuck on a 2021
/// ffmpeg 4.4.1 build and had no newer release), so ffmpeg is expected to come from the OS instead:
/// a pinned static build on PATH in the Docker image, or on PATH for local dev (e.g. `winget install
/// Gyan.FFmpeg` on Windows). The config escape hatch below still lets a specific install be pinned
/// without touching PATH.</summary>
public interface IFfmpegBinaryResolver
{
    /// <summary>True once both ffmpeg and ffprobe have been located, so callers (the merge wizard
    /// step) can disable themselves with a clear message instead of throwing.</summary>
    bool IsAvailable { get; }
    string? FfmpegPath { get; }
    string? FfprobePath { get; }
}

public class FfmpegBinaryResolver(IConfiguration configuration) : IFfmpegBinaryResolver
{
    public bool IsAvailable => FfmpegPath is not null && FfprobePath is not null;
    public string? FfmpegPath { get; } = Resolve(configuration["Ffmpeg:Path"], "ffmpeg");
    public string? FfprobePath { get; } = Resolve(configuration["Ffmpeg:ProbePath"], "ffprobe");

    private static string? Resolve(string? configuredPath, string binaryName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var exeSuffix = OperatingSystem.IsWindows() ? ".exe" : "";
        return FindOnPath(binaryName + exeSuffix);
    }

    private static string? FindOnPath(string fileName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable)) return null;

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (directory.Length == 0) continue;
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
