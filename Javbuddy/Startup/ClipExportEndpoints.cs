using Javbuddy.Services.ClipExport;

namespace Javbuddy.Startup;

/// <summary>The download of a clip prepared by IClipExportService. Like the rest of the app it has no authentication of its own;
/// the token is random and single-use.</summary>
public static class ClipExportEndpoints
{
    public static void MapClipExportEndpoints(this WebApplication app) =>
        app.MapGet("/api/clip-exports/{token}", (string token, ClipExportDownloads downloads) => Download(token, downloads));

    /// <summary>The clip for <paramref name="token"/> as an attachment, or 404 when the token is unknown, used or expired. The token
    /// is only a key into ClipExportDownloads (never joined into a path), so it can't reach any other file. The stream deletes the
    /// temp file when the response is done with it, finished or aborted.</summary>
    public static IResult Download(string token, ClipExportDownloads downloads)
    {
        if (downloads.Take(token) is not { } download) return Results.NotFound();
        FileStream stream;
        try
        {
            stream = new FileStream(download.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        }
        catch (FileNotFoundException)
        {
            // Swept between Take's check and here.
            return Results.NotFound();
        }
        return Results.File(stream, download.ContentType, download.FileName);
    }
}
