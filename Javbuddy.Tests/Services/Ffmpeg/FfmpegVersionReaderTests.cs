using Javbuddy.Services.Ffmpeg;

namespace Javbuddy.Tests.Services.Ffmpeg;

public sealed class FfmpegVersionReaderTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("javbuddy-ffmpeg-version-");

    public void Dispose() => dir.Delete(recursive: true);

    [Theory]
    [InlineData("ffmpeg version 7.1.1-static https://johnvansickle.com/ffmpeg/  Copyright (c) 2000-2025\nbuilt with gcc", "7.1.1")]
    [InlineData("ffmpeg version 6.0-6ubuntu1 Copyright (c) 2000-2023", "6.0")]
    [InlineData("ffmpeg version n6.1.2 Copyright", "6.1.2")]
    [InlineData("ffmpeg version 8 Copyright", "8")]
    [InlineData("ffmpeg version N-113345-gabcdef Copyright", "git")]
    [InlineData("ffmpeg version weird-build Copyright", "unknown")]
    [InlineData("FFmpeg version 7.0 Copyright", "7.0")]
    [InlineData("not ffmpeg at all", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseVersion_ReturnsALowCardinalityVersion(string? output, string? expected) =>
        Assert.Equal(expected, FfmpegVersionReader.ParseVersion(output));

    private string Script(string body)
    {
        var path = Path.Combine(dir.FullName, "ffmpeg");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public async Task ReadAsync_RunsTheBinaryAndParsesItsVersion()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffmpeg = Script("echo 'ffmpeg version 7.1.1-static Copyright'");

        Assert.Equal("7.1.1", await FfmpegVersionReader.ReadAsync(ffmpeg, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_NonZeroExit_ReturnsNull()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffmpeg = Script("echo 'ffmpeg version 7.1.1'; exit 1");

        Assert.Null(await FfmpegVersionReader.ReadAsync(ffmpeg, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_MissingBinary_ReturnsNullInsteadOfThrowing() =>
        Assert.Null(await FfmpegVersionReader.ReadAsync(Path.Combine(dir.FullName, "nope"), CancellationToken.None));

    [Fact]
    public async Task ReadAsync_CallerCancellation_StillPropagates()
    {
        if (OperatingSystem.IsWindows()) return;
        var ffmpeg = Script("exec sleep 30");
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FfmpegVersionReader.ReadAsync(ffmpeg, cts.Token));
    }
}
