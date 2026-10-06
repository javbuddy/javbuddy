using Bunit;
using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class CropperViewportTests : BunitContext
{
    [Fact]
    public void Renders_ImageAndCropBoxWithAllEightHandles()
    {
        var cut = Render<CropperViewport>();

        Assert.NotNull(cut.Find(".cropper-viewport img"));
        var handles = cut.FindAll(".cropper-crop-box .crop-handle").Select(h => h.GetAttribute("data-handle"));
        Assert.Equal(["nw", "ne", "sw", "se", "n", "s", "w", "e"], handles);
    }

    [Theory]
    [InlineData(null, "height: 400px")]
    [InlineData(380, "height: 380px")]
    public void HeightPx_SetsViewportHeight(int? height, string expectedStyle)
    {
        var cut = Render<CropperViewport>(p =>
        {
            if (height is { } h) p.Add(c => c.HeightPx, h);
        });

        Assert.Contains(expectedStyle, cut.Find(".cropper-viewport").GetAttribute("style"));
    }
}
