using Javbuddy.Services.Images;

namespace Javbuddy.Tests.Services.Images;

public class PosterCropGeometryTests
{
    [Fact]
    public void LandscapeCover_CropsRightPortionOfWidth_FullHeight()
    {
        // A typical full JAV DVD-case scan, clearly landscape (aspect ratio > 1.2).
        var (left, top, right, bottom) = PosterCropGeometry.ComputeCropRect(1895, 1000);

        Assert.Equal(0, top);
        Assert.Equal(1000, bottom);
        Assert.Equal(1895, right);
        // ~47.2% of the width kept on the right, per the class's own documented constant.
        Assert.InRange(left, 990, 1000);
    }

    [Fact]
    public void SquareCover_CenterCropsToPosterAspectRatio()
    {
        var (left, top, right, bottom) = PosterCropGeometry.ComputeCropRect(1000, 1000);

        var croppedWidth = right - left;
        var croppedHeight = bottom - top;
        Assert.Equal(1000, croppedHeight); // full height kept, width narrowed
        Assert.InRange((double)croppedWidth / croppedHeight, 0.66, 0.67); // ~2:3
        Assert.True(left > 0 && right < 1000); // centered horizontally
    }

    [Fact]
    public void TallPortraitCover_CenterCropsToPosterAspectRatio()
    {
        // Already narrower than a 2:3 poster (aspect ratio < 2/3) — width is kept, height is cropped.
        var (left, top, right, bottom) = PosterCropGeometry.ComputeCropRect(400, 1200);

        var croppedWidth = right - left;
        var croppedHeight = bottom - top;
        Assert.Equal(400, croppedWidth);
        Assert.InRange((double)croppedWidth / croppedHeight, 0.66, 0.67);
        Assert.True(top > 0 && bottom < 1200); // centered vertically
    }

    [Fact]
    public void ExactPosterAspectRatio_NoCropNeeded()
    {
        var (left, top, right, bottom) = PosterCropGeometry.ComputeCropRect(400, 600);

        Assert.Equal(0, left);
        Assert.Equal(0, top);
        Assert.Equal(400, right);
        Assert.Equal(600, bottom);
    }
}
