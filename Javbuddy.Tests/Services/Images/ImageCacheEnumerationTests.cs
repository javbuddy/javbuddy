using Javbuddy.Services.Images;

namespace Javbuddy.Tests.Services.Images;

public class ImageCacheEnumerationTests
{
    [Fact]
    public void EnumerateFileInfos_ReturnsOnlyCacheFiles_FromNestedShardsInOnePass()
    {
        var root = Directory.CreateTempSubdirectory("javbuddy-enum-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "ab", "cd"));
            File.WriteAllBytes(Path.Combine(root.FullName, "a.webp"), new byte[3]);
            File.WriteAllBytes(Path.Combine(root.FullName, "ab", "b.WEBM"), new byte[5]);
            File.WriteAllBytes(Path.Combine(root.FullName, "ab", "cd", "c.jpg"), new byte[7]);
            File.WriteAllBytes(Path.Combine(root.FullName, "ab", "notes.txt"), new byte[11]);
            File.WriteAllBytes(Path.Combine(root.FullName, "d.webp.tmp"), new byte[13]);

            var files = ILocalImageCache.EnumerateFileInfos(root.FullName).ToList();

            Assert.Equal(["a.webp", "b.WEBM", "c.jpg"], files.Select(f => f.Name).Order(StringComparer.OrdinalIgnoreCase));
            Assert.Equal(15, files.Sum(f => f.Length));
            Assert.Equal(files.Select(f => f.FullName).Order(), ILocalImageCache.EnumerateFiles(root.FullName).Order());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
