namespace Javbuddy.Services.Images;

public static class ImageContentType
{
    /// <summary>The image format from its first bytes (JPEG, PNG, GIF, else WebP, the format Javbuddy's own images use).</summary>
    public static string Sniff(byte[] bytes) => bytes switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0x47, 0x49, 0x46, ..] => "image/gif",
        _ => "image/webp",
    };
}
