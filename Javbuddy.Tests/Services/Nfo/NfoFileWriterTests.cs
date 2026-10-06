using System.Text;
using System.Xml.Linq;
using Javbuddy.Services.Nfo;

namespace Javbuddy.Tests.Services.Nfo;

public sealed class NfoFileWriterTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "javbuddy-nfowriter-" + Guid.NewGuid().ToString("N"));

    public NfoFileWriterTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(dir, recursive: true);
    }

    private string CreateNfo(string content)
    {
        var path = Path.Combine(dir, "ABC-123.nfo");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task WriteTextAsync_ReplacesContentAndReturnsPreviousContent()
    {
        var path = CreateNfo("<movie>old</movie>");

        var result = await NfoFileWriter.WriteTextAsync(path, "<movie>new</movie>", expectedContent: null, CancellationToken.None);

        Assert.Equal(NfoWriteStatus.Saved, result.Status);
        Assert.Equal("<movie>new</movie>", File.ReadAllText(path));
        Assert.Equal("<movie>old</movie>", result.PreviousContent);
    }

    [Fact]
    public async Task WriteTextAsync_LeavesNoBackupOrTempFileInTheFolder()
    {
        var path = CreateNfo("<movie>v1</movie>");

        await NfoFileWriter.WriteTextAsync(path, "<movie>v2</movie>", null, CancellationToken.None);
        await NfoFileWriter.WriteTextAsync(path, "<movie>v3</movie>", null, CancellationToken.None);

        Assert.Equal("<movie>v3</movie>", File.ReadAllText(path));
        Assert.Equal(["ABC-123.nfo"], Directory.EnumerateFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task WriteTextAsync_KeepsOriginalUnixFileMode()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = CreateNfo("<movie>old</movie>");
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead;
        File.SetUnixFileMode(path, mode);

        await NfoFileWriter.WriteTextAsync(path, "<movie>new</movie>", null, CancellationToken.None);

        Assert.Equal(mode, File.GetUnixFileMode(path));
    }

    [Fact]
    public async Task WriteTextAsync_ReturnsFingerprintOfWrittenFile()
    {
        var path = CreateNfo("<movie>old</movie>");

        var result = await NfoFileWriter.WriteTextAsync(path, "<movie>longer content</movie>", null, CancellationToken.None);

        var info = new FileInfo(path);
        Assert.Equal(info.LastWriteTimeUtc, result.LastWriteUtc);
        Assert.Equal(info.Length, result.Size);
    }

    [Fact]
    public async Task WriteTextAsync_WritesUtf8WithoutBom()
    {
        var path = CreateNfo("<movie />");

        await NfoFileWriter.WriteTextAsync(path, "<title>日本語</title>", null, CancellationToken.None);

        Assert.Equal(new UTF8Encoding(false).GetBytes("<title>日本語</title>"), File.ReadAllBytes(path));
    }

    [Fact]
    public async Task WriteTextAsync_ReturnsFileChangedWithoutWriting_WhenContentDiffersFromExpected()
    {
        var path = CreateNfo("<movie>edited externally</movie>");

        var result = await NfoFileWriter.WriteTextAsync(path, "<movie>new</movie>", "<movie>old</movie>", CancellationToken.None);

        Assert.Equal(NfoWriteStatus.FileChanged, result.Status);
        Assert.Equal("<movie>edited externally</movie>", File.ReadAllText(path));
        Assert.Null(result.PreviousContent);
        Assert.Equal(["ABC-123.nfo"], Directory.EnumerateFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task WriteTextAsync_Saves_WhenContentMatchesExpected()
    {
        var path = CreateNfo("<movie>old</movie>");

        var result = await NfoFileWriter.WriteTextAsync(path, "<movie>new</movie>", "<movie>old</movie>", CancellationToken.None);

        Assert.Equal(NfoWriteStatus.Saved, result.Status);
        Assert.Equal("<movie>new</movie>", File.ReadAllText(path));
        Assert.Equal("<movie>old</movie>", result.PreviousContent);
    }

    [Fact]
    public async Task WriteTextAsync_RefusesReadOnlyFile()
    {
        var path = CreateNfo("<movie>old</movie>");
        new FileInfo(path).IsReadOnly = true;

        var result = await NfoFileWriter.WriteTextAsync(path, "<movie>new</movie>", null, CancellationToken.None);

        Assert.Equal(NfoWriteStatus.ReadOnly, result.Status);
        Assert.Equal("<movie>old</movie>", File.ReadAllText(path));
        Assert.Equal(["ABC-123.nfo"], Directory.EnumerateFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task WriteTextAsync_RefusesMissingFile()
    {
        var path = Path.Combine(dir, "ABC-123.nfo");

        var result = await NfoFileWriter.WriteTextAsync(path, "<movie>new</movie>", null, CancellationToken.None);

        Assert.Equal(NfoWriteStatus.NotFound, result.Status);
        Assert.Empty(Directory.EnumerateFiles(dir));
    }

    [Fact]
    public async Task WriteXmlAsync_CleansUpTempFileAndLeavesOriginal_WhenWriteFails()
    {
        var path = CreateNfo("<movie>old</movie>");
        // A control character XmlWriter refuses makes the write fail after the temp file exists.
        var doc = new XDocument(new XElement("movie", "bad \u0001 char"));

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            NfoFileWriter.WriteXmlAsync(path, doc, CancellationToken.None));

        Assert.Equal("<movie>old</movie>", File.ReadAllText(path));
        Assert.Equal(["ABC-123.nfo"], Directory.EnumerateFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task WriteXmlAsync_PreservesDeclarationAndWhitespaceWithoutReindenting()
    {
        const string original = "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>\n<movie>\n  <title>Old</title>\n</movie>";
        var path = CreateNfo(original);
        var doc = XDocument.Parse(original, LoadOptions.PreserveWhitespace);
        doc.Root!.Element("title")!.Value = "New";

        var result = await NfoFileWriter.WriteXmlAsync(path, doc, CancellationToken.None);

        Assert.Equal(NfoWriteStatus.Saved, result.Status);
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>\n<movie>\n  <title>New</title>\n</movie>",
            Encoding.UTF8.GetString(bytes));
        Assert.Equal(original, result.PreviousContent);
    }
}
