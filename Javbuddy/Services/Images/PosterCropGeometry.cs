namespace Javbuddy.Services.Images;

/// <summary>Ports javinizer-go's cover-to-poster crop geometry (internal/imageutil/crop.go,
/// CropPosterFromCover) so a poster we crop ourselves (see RemotePosterCropService) looks the same
/// as one javinizer-go crops in its own local-file pipeline. Landscape covers (a typical full JAV
/// DVD-case scan — front, spine, and back all in one image) crop the right ~47.2% of the width,
/// keeping the front cover; square/portrait covers center-crop to a 2:3 poster aspect ratio.</summary>
public static class PosterCropGeometry
{
    private const double LandscapeAspectRatioThreshold = 1.2;

    // Keeps the right 47.2% of the width — javinizer-go's original constant.
    private const double RightCropDivisor = 1.895734597;

    private const double PosterAspectRatio = 2.0 / 3.0;

    public static (int Left, int Top, int Right, int Bottom) ComputeCropRect(int width, int height)
    {
        var aspectRatio = (double)width / height;

        if (aspectRatio > LandscapeAspectRatioThreshold)
        {
            var left = (int)(width / RightCropDivisor);
            return (left, 0, width, height);
        }

        int cropWidth, cropHeight;
        if (aspectRatio > PosterAspectRatio)
        {
            cropHeight = height;
            cropWidth = (int)(cropHeight * PosterAspectRatio);
        }
        else
        {
            cropWidth = width;
            cropHeight = (int)(cropWidth / PosterAspectRatio);
        }

        var cropLeft = (width - cropWidth) / 2;
        var cropTop = (height - cropHeight) / 2;
        return (cropLeft, cropTop, cropLeft + cropWidth, cropTop + cropHeight);
    }
}
