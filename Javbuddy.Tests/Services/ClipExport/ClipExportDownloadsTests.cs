using Javbuddy.Services.ClipExport;
using Javbuddy.Services.Images;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Javbuddy.Tests.Services.ClipExport;

public sealed class ClipExportDownloadsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"javbuddy-clip-downloads-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider time = new(DateTimeOffset.UtcNow);
    private readonly ClipExportDownloads downloads;

    public ClipExportDownloadsTests()
    {
        var cache = Substitute.For<ILocalImageCache>();
        cache.RootPath.Returns(root);
        downloads = new ClipExportDownloads(cache, time);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private string WriteClip()
    {
        var path = downloads.NewTempPath();
        File.WriteAllText(path, "clip");
        return path;
    }

    [Fact]
    public void NewTempPath_IsInTheCacheClipExportsFolder_WithAnExtensionCacheMaintenanceIgnores()
    {
        var path = downloads.NewTempPath();

        Assert.Equal(Path.Combine(root, "clip-exports"), Path.GetDirectoryName(path));
        Assert.Equal(".clip", Path.GetExtension(path));
        File.WriteAllText(path, "clip");
        Assert.Empty(ILocalImageCache.EnumerateFileInfos(root));
        Assert.True(Directory.Exists(downloads.Folder));
    }

    [Fact]
    public void Take_ReturnsTheClipOnce()
    {
        var path = WriteClip();
        var token = downloads.Add(path, "ABC-123 - Scene 1.mp4", "video/mp4");

        var taken = downloads.Take(token);

        Assert.NotNull(taken);
        Assert.Equal(path, taken.Path);
        Assert.Equal("ABC-123 - Scene 1.mp4", taken.FileName);
        Assert.Equal("video/mp4", taken.ContentType);
        Assert.Null(downloads.Take(token));
    }

    [Fact]
    public void Take_UnknownToken_IsNull()
    {
        Assert.Null(downloads.Take("nope"));
    }

    [Fact]
    public void Take_AfterTheLifetime_IsNull_AndDeletesTheFile()
    {
        var path = WriteClip();
        var token = downloads.Add(path, "a.mp4", "video/mp4");
        time.Advance(ClipExportDownloads.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(downloads.Take(token));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Sweep_DeletesExpiredClips_AndOldLeftovers_KeepsFreshOnes()
    {
        var expired = WriteClip();
        var expiredToken = downloads.Add(expired, "old.mp4", "video/mp4");
        var leftover = WriteClip();
        File.SetLastWriteTimeUtc(leftover, time.GetUtcNow().UtcDateTime - ClipExportDownloads.Lifetime - TimeSpan.FromMinutes(1));
        time.Advance(ClipExportDownloads.Lifetime + TimeSpan.FromSeconds(1));
        var fresh = WriteClip();
        File.SetLastWriteTimeUtc(fresh, time.GetUtcNow().UtcDateTime);
        var freshToken = downloads.Add(fresh, "new.mp4", "video/mp4");

        downloads.Sweep();

        Assert.False(File.Exists(expired));
        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(fresh));
        Assert.Null(downloads.Take(expiredToken));
        Assert.NotNull(downloads.Take(freshToken));
    }
}
