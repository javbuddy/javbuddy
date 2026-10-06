namespace Javbuddy.Services.Images;

/// <summary>
/// Represents a normalized crop rectangle within an image, where coordinates and dimensions
/// are expressed as fractions (0.0 to 1.0) of the original image's width and height.
/// </summary>
public record NormalizedCropRect(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// Clamps and validates the normalized crop coordinates to guarantee they fall within [0.0, 1.0].
    /// </summary>
    public NormalizedCropRect Clamp()
    {
        var x = Math.Clamp(X, 0.0, 1.0);
        var y = Math.Clamp(Y, 0.0, 1.0);
        var w = Math.Clamp(Width, 0.001, 1.0 - x);
        var h = Math.Clamp(Height, 0.001, 1.0 - y);
        return new NormalizedCropRect(x, y, w, h);
    }
}
