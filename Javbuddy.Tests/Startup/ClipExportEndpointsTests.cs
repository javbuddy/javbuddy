using System.Text;
using Javbuddy.Services.ClipExport;
using Javbuddy.Services.Images;
using Javbuddy.Startup;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Startup;

public sealed class ClipExportEndpointsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"javbuddy-clip-endpoint-{Guid.NewGuid():N}");
    private readonly ClipExportDownloads downloads;

    public ClipExportEndpointsTests()
    {
        var cache = Substitute.For<ILocalImageCache>();
        cache.RootPath.Returns(root);
        downloads = new ClipExportDownloads(cache);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private (string Token, string Path) Prepare(string fileName, string content = "clip bytes")
    {
        var path = downloads.NewTempPath();
        File.WriteAllText(path, content);
        return (downloads.Add(path, fileName, "video/mp4"), path);
    }

    private static async Task<DefaultHttpContext> ExecuteAsync(IResult result)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();
        await result.ExecuteAsync(http);
        return http;
    }

    [Fact]
    public async Task UnknownToken_Is404()
    {
        var http = await ExecuteAsync(ClipExportEndpoints.Download("nope", downloads));

        Assert.Equal(StatusCodes.Status404NotFound, http.Response.StatusCode);
    }

    [Fact]
    public async Task Download_StreamsTheClipAsAnAttachment_DeletesIt_AndTheTokenIsSingleUse()
    {
        var (token, path) = Prepare("ABC-123 - 浴室 [00-01-00 - 00-02-00].mp4");

        var http = await ExecuteAsync(ClipExportEndpoints.Download(token, downloads));

        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
        Assert.Equal("video/mp4", http.Response.ContentType);
        Assert.Equal("clip bytes", Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray()));
        var disposition = http.Response.Headers.ContentDisposition.ToString();
        Assert.StartsWith("attachment;", disposition);
        Assert.Contains("filename*=UTF-8''ABC-123%20-%20%E6%B5%B4%E5%AE%A4", disposition);
        Assert.False(File.Exists(path));

        var again = await ExecuteAsync(ClipExportEndpoints.Download(token, downloads));
        Assert.Equal(StatusCodes.Status404NotFound, again.Response.StatusCode);
    }
}
