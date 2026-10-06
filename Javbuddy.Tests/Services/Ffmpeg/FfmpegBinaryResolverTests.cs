using Javbuddy.Services.Ffmpeg;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Ffmpeg;

public class FfmpegBinaryResolverTests
{
    private static bool IsWindows => OperatingSystem.IsWindows();
    private static string ExeSuffix => IsWindows ? ".exe" : "";

    [Fact]
    public void Resolve_ConfiguredPathExists_TakesPriorityOverPath()
    {
        using var scratch = new ScratchDirectory();
        var configuredFfmpeg = scratch.WriteFile("custom", "my-ffmpeg" + ExeSuffix);
        var configuredFfprobe = scratch.WriteFile("custom", "my-ffprobe" + ExeSuffix);
        var pathFfmpeg = scratch.WriteFile("on-path", "ffmpeg" + ExeSuffix); // would otherwise win

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ffmpeg:Path"] = configuredFfmpeg,
                ["Ffmpeg:ProbePath"] = configuredFfprobe,
            })
            .Build();

        var resolver = WithPathOverride([System.IO.Path.GetDirectoryName(pathFfmpeg)!], () => new FfmpegBinaryResolver(config));

        Assert.Equal(configuredFfmpeg, resolver.FfmpegPath);
        Assert.Equal(configuredFfprobe, resolver.FfprobePath);
        Assert.True(resolver.IsAvailable);
    }

    [Fact]
    public void Resolve_FoundOnPath_IsAvailable()
    {
        using var scratch = new ScratchDirectory();
        var expectedFfmpeg = scratch.WriteFile(null, "ffmpeg" + ExeSuffix);
        scratch.WriteFile(null, "ffprobe" + ExeSuffix);

        var resolver = WithPathOverride([scratch.Path], () => new FfmpegBinaryResolver(new ConfigurationBuilder().Build()));

        Assert.Equal(expectedFfmpeg, resolver.FfmpegPath);
        Assert.True(resolver.IsAvailable);
    }

    [Fact]
    public void Resolve_NothingOnPath_IsNotAvailable()
    {
        using var scratch = new ScratchDirectory(); // empty — nothing named ffmpeg/ffprobe in it

        var resolver = WithPathOverride([scratch.Path], () => new FfmpegBinaryResolver(new ConfigurationBuilder().Build()));

        Assert.Null(resolver.FfmpegPath);
        Assert.Null(resolver.FfprobePath);
        Assert.False(resolver.IsAvailable);
    }

    /// <summary>Runs resolve with PATH replaced by exactly the given directories, so the dev
    /// machine's own real ffmpeg install (if any) can't leak into a "not found" assertion, and a
    /// scratch directory reliably counts as "found" instead of depending on the real PATH's
    /// unpredictable contents.</summary>
    private static FfmpegBinaryResolver WithPathOverride(IReadOnlyList<string> directories, Func<FfmpegBinaryResolver> resolve)
    {
        var original = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, directories));
        try
        {
            return resolve();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
        }
    }

    private sealed class ScratchDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("javbuddy-ffmpeg-resolver-test-").FullName;

        public string WriteFile(string? subdirectory, string fileName)
        {
            var dir = subdirectory is null ? Path : System.IO.Path.Combine(Path, subdirectory);
            Directory.CreateDirectory(dir);
            var fullPath = System.IO.Path.Combine(dir, fileName);
            File.WriteAllText(fullPath, "");
            return fullPath;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
