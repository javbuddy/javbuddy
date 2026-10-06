using System.Net;

namespace Javbuddy.Services.Images;

/// <summary>User-facing wording for a failed <see cref="RemoteImageDownloader"/> download, shared by
/// the "import from URL" flows for actor portraits and movie extrafanart.</summary>
public static class RemoteImageDownloadMessages
{
    public const string BlockedAddress = "Importing from a local or private network address isn't allowed. Save the image and upload the file instead.";

    public static string Describe(RemoteImageDownloadResult download, int maxSizeMb) => download.Failure switch
    {
        RemoteImageDownloadFailure.MissingUrl => "Please enter an image URL.",
        RemoteImageDownloadFailure.DefunctHost => "The provided image URL is on a defunct domain (r18.com) and cannot be downloaded.",
        RemoteImageDownloadFailure.InvalidUrl => "Please enter a valid HTTP or HTTPS image URL.",
        RemoteImageDownloadFailure.BlockedAddress => BlockedAddress,
        RemoteImageDownloadFailure.Timeout => "Request timed out while downloading the image.",
        RemoteImageDownloadFailure.SendFailed when download.Error is HttpRequestException => $"Failed to download image from the remote server: {download.Error.Message}",
        RemoteImageDownloadFailure.SendFailed => $"Error downloading image: {download.Error?.Message}",
        RemoteImageDownloadFailure.HttpStatus when download.StatusCode == HttpStatusCode.NotFound => "Image not found at the specified URL (HTTP 404).",
        RemoteImageDownloadFailure.HttpStatus => $"Failed to download image (HTTP {(int)download.StatusCode!.Value}: {download.ReasonPhrase}).",
        RemoteImageDownloadFailure.DeclaredTooLarge or RemoteImageDownloadFailure.TooLarge => $"Image size exceeds {maxSizeMb} MB limit.",
        RemoteImageDownloadFailure.UnacceptableMediaType => $"The URL did not return an image (received '{download.MediaType}'). Please provide a direct link to a JPEG, PNG, or WebP image.",
        RemoteImageDownloadFailure.ReadFailed => $"Error reading image content: {download.Error?.Message}",
        _ => "The downloaded image was empty.",
    };
}
