using Javbuddy.Services.DeoVr;
using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.DeoVr;

public class DeoVrProjectionTests
{
    [Theory]
    [InlineData(VrFormat.Vr180Sbs, "dome", "sbs")]
    [InlineData(VrFormat.Vr180Tb, "dome", "tb")]
    [InlineData(VrFormat.Vr180, "dome", "off")]
    [InlineData(VrFormat.Vr360Sbs, "sphere", "sbs")]
    [InlineData(VrFormat.Vr360Tb, "sphere", "tb")]
    [InlineData(VrFormat.Vr360, "sphere", "off")]
    [InlineData(VrFormat.FisheyeSbs, "fisheye", "sbs")]
    [InlineData(VrFormat.FisheyeTb, "fisheye", "tb")]
    [InlineData(VrFormat.Fisheye, "fisheye", "off")]
    [InlineData(VrFormat.Vr, "dome", "sbs")]
    [InlineData(VrFormat.ThreeDHsbs, "flat", "sbs")]
    [InlineData(VrFormat.ThreeDFsbs, "flat", "sbs")]
    [InlineData(VrFormat.ThreeDSbs, "flat", "sbs")]
    [InlineData(VrFormat.ThreeD, "flat", "sbs")]
    [InlineData(VrFormat.ThreeDHtab, "flat", "tb")]
    [InlineData(VrFormat.ThreeDFtab, "flat", "tb")]
    [InlineData(VrFormat.ThreeDTab, "flat", "tb")]
    [InlineData(VrFormat.ThreeDMvc, "flat", "off")]
    [InlineData(null, "flat", "off")]
    [InlineData("nonsense", "flat", "off")]
    public void EachVrFormat_MapsToItsDeoVrProjection(string? vrType, string expectedScreen, string expectedStereo) =>
        Assert.Equal(new DeoVrProjectionInfo(expectedScreen, expectedStereo), DeoVrProjection.FromVrType(vrType, "ABC-001.mp4"));

    [Fact]
    public void EveryVrFormat_IsMapped()
    {
        // A format added to VrFormat without a mapping here would silently play flat.
        Assert.All(VrFormat.All.Where(f => f != VrFormat.ThreeDMvc),
            f => Assert.NotEqual(new DeoVrProjectionInfo("flat", "off"), DeoVrProjection.FromVrType(f, null)));
    }

    [Theory]
    [InlineData("SIVR-123 [MKX200].mkv", "mkx200")]
    [InlineData("SIVR-123_rf52_LR.mp4", "rf52")]
    [InlineData("SIVR-123_fisheye190_LR.mp4", "fisheye")]
    [InlineData(null, "fisheye")]
    public void AFisheyeFormat_TakesItsLensFromTheFileName(string? fileName, string expectedScreen) =>
        Assert.Equal(new DeoVrProjectionInfo(expectedScreen, "sbs"), DeoVrProjection.FromVrType(VrFormat.FisheyeSbs, fileName));

    [Fact]
    public void ALensFlag_DoesNotChangeANonFisheyeFormat()
    {
        Assert.Equal(new DeoVrProjectionInfo("dome", "sbs"), DeoVrProjection.FromVrType(VrFormat.Vr180Sbs, "SIVR-123_MKX200.mp4"));
        Assert.Equal(new DeoVrProjectionInfo("flat", "off"), DeoVrProjection.FromVrType(null, "SIVR-123_RF52.mp4"));
    }
}
