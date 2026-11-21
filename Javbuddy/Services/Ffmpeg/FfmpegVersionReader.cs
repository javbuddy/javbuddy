using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Javbuddy.Services.Ffmpeg;

/// <summary>Reads the ffmpeg build's version for the <c>javbuddy_ffmpeg_binary_info</c> metric by running <c>ffmpeg -version</c>.
/// Never throws: any failure (not executable, timeout, unreadable output) yields null.</summary>
public static partial class FfmpegVersionReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [GeneratedRegex(@"^ffmpeg version (\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionLinePattern();

    [GeneratedRegex(@"^n?(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase)]
    private static partial Regex ReleasePattern();

    /// <summary>Low-cardinality version from the first line of <c>ffmpeg -version</c> output: the numeric release
    /// ("ffmpeg version 7.1.1-static ..." gives "7.1.1", "n6.1.2" gives "6.1.2"), "git" for a development build
    /// ("N-113345-gabc"), "unknown" for anything else, and null when there is no version line at all.</summary>
    public static string? ParseVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var firstLine = output.Split('\n', 2)[0].Trim();
        if (VersionLinePattern().Match(firstLine) is not { Success: true } line) return null;

        var token = line.Groups[1].Value;
        if (ReleasePattern().Match(token) is { Success: true } release) return release.Groups[1].Value;
        return token.StartsWith("N-", StringComparison.Ordinal) ? "git" : "unknown";
    }

    public static async Task<string?> ReadAsync(string ffmpegPath, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            var startInfo = new ProcessStartInfo(ffmpegPath, "-version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(startInfo);
            if (process is null) return null;
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                return process.ExitCode == 0 ? ParseVersion(output) : null;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // Timed out.
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
