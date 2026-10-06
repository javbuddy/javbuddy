namespace Javbuddy.Services.Movies;

/// <summary>
/// Formats the compact quality label used by movie posters and detail badges. MediaInfo scan
/// types other than Progressive are presented as an interlaced height (for example, 1080i), so
/// MBAFF and Mixed media are conspicuous alongside ordinary interlaced files.
/// </summary>
public static class MediaQualityLabelFormatter
{
    public static string? FormatPoster(int? width, int? height, string? scanType)
    {
        if (width is null)
        {
            return null;
        }

        if (IsNonProgressive(scanType) && height is > 0)
        {
            return $"{height}i";
        }

        return width switch
        {
            < 1280 => "SD",
            < 3840 => "HD",
            < 7680 => "4K",
            _ => "8K",
        };
    }

    public static string? FormatDetail(int? width, int? height, string? scanType)
    {
        if (width is not > 0 || height is not > 0)
        {
            return null;
        }

        return IsNonProgressive(scanType)
            ? $"{height}i"
            : $"{width}x{height}";
    }

    private static bool IsNonProgressive(string? scanType) =>
        !string.IsNullOrWhiteSpace(scanType) && !scanType.Trim().Equals("Progressive", StringComparison.OrdinalIgnoreCase);
}
