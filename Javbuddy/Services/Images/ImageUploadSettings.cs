namespace Javbuddy.Services.Images;

/// <summary>Controls the maximum permitted file size and batch file limit for actor photo uploads and custom actor portrait uploads/imports.
/// Configured via ImageUpload:MaxSizeMb (default 20 MB) and ImageUpload:MaxBatchFiles (default 500).</summary>
public sealed record ImageUploadSettings(
    int MaxSizeMb = ImageUploadSettings.DefaultMaxSizeMb,
    int MaxBatchFiles = ImageUploadSettings.DefaultMaxBatchFiles)
{
    public const int DefaultMaxSizeMb = 20;
    public const int DefaultMaxBatchFiles = 500;

    public static ImageUploadSettings Default { get; } = new(DefaultMaxSizeMb, DefaultMaxBatchFiles);

    public long MaxSizeBytes => (long)MaxSizeMb * 1024L * 1024L;

    public static ImageUploadSettings FromConfiguration(IConfiguration configuration)
    {
        var sizeRaw = configuration["ImageUpload:MaxSizeMb"];
        var maxSizeMb = int.TryParse(sizeRaw, out var parsedMb) && parsedMb > 0
            ? parsedMb
            : DefaultMaxSizeMb;

        var filesRaw = configuration["ImageUpload:MaxBatchFiles"];
        var maxBatchFiles = int.TryParse(filesRaw, out var parsedFiles) && parsedFiles > 0
            ? parsedFiles
            : DefaultMaxBatchFiles;

        return new ImageUploadSettings(maxSizeMb, maxBatchFiles);
    }
}
