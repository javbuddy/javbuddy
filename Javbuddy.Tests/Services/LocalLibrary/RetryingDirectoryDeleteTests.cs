using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Tests.Services.LocalLibrary;

public class RetryingDirectoryDeleteTests
{
    private static readonly TimeSpan[] NoWaitDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

    [Fact]
    public async Task DeleteAsync_RetriesAfterDirectoryNotEmpty_UntilItSucceeds()
    {
        var attempts = 0;
        void Delete(string _)
        {
            attempts++;
            // What Directory.Delete(recursive) throws when the final rmdir hits ENOTEMPTY — e.g. an
            // NFS .nfsXXXX silly-rename file left while Jellyfin still streams the video.
            if (attempts < 3) throw new IOException("Directory not empty : '/media/jav/START-503'");
        }

        await RetryingDirectoryDelete.DeleteAsync("/media/jav/START-503", NoWaitDelays, Delete, _ => true);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task DeleteAsync_StillFailingAfterEveryRetry_RethrowsTheLastError()
    {
        var attempts = 0;
        void Delete(string _)
        {
            attempts++;
            throw new IOException($"Directory not empty (attempt {attempts})");
        }

        var ex = await Assert.ThrowsAsync<IOException>(() =>
            RetryingDirectoryDelete.DeleteAsync("/x", NoWaitDelays, Delete, _ => true));

        Assert.Equal(NoWaitDelays.Length + 1, attempts);
        Assert.Equal("Directory not empty (attempt 4)", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_FolderGoneBeforeARetry_StopsWithoutError()
    {
        var attempts = 0;
        void Delete(string _)
        {
            attempts++;
            throw new IOException("Directory not empty");
        }

        await RetryingDirectoryDelete.DeleteAsync("/x", NoWaitDelays, Delete, _ => false);

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task DeleteAsync_PermissionDenied_IsNotRetried()
    {
        var attempts = 0;
        void Delete(string _)
        {
            attempts++;
            throw new UnauthorizedAccessException("Access denied");
        }

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            RetryingDirectoryDelete.DeleteAsync("/x", NoWaitDelays, Delete, _ => true));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task DeleteAsync_RealFolder_IsDeletedRecursively()
    {
        var folder = Path.Combine(Path.GetTempPath(), "javbuddy-retry-delete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "extrafanart"));
        await File.WriteAllTextAsync(Path.Combine(folder, "movie.mp4"), "video");
        await File.WriteAllTextAsync(Path.Combine(folder, "extrafanart", "1.jpg"), "img");

        await RetryingDirectoryDelete.DeleteAsync(folder, NoWaitDelays);

        Assert.False(Directory.Exists(folder));
    }
}
