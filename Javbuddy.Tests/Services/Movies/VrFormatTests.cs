using Javbuddy.Services.Movies;

namespace Javbuddy.Tests.Services.Movies;

public class VrFormatTests
{
    [Theory]
    // Jellyfin 3D layouts, with or without the Kodi-style "3d" token.
    [InlineData("SIVR-059.3d.hsbs.mp4", "3D HSBS")]
    [InlineData("SIVR-059.3d.sbs.mp4", "3D SBS")]
    [InlineData("SIVR-059.fsbs.mkv", "3D FSBS")]
    [InlineData("SIVR-059.3D.HTAB.mp4", "3D HTAB")]
    [InlineData("SIVR-059 [TAB].mp4", "3D TAB")]
    [InlineData("SIVR-059_OU.mp4", "3D TAB")]
    [InlineData("SIVR-059_HOU.mp4", "3D HTAB")]
    [InlineData("SIVR-059.ftab.mp4", "3D FTAB")]
    [InlineData("SIVR-059.sbs3d.mp4", "3D SBS")]
    [InlineData("SIVR-059.mvc.mkv", "3D MVC")]
    [InlineData("SIVR-059.3d.mkv", "3D")]
    // VR projections win over a plain layout; their layout is SBS or TB.
    [InlineData("SIVR-059_180_sbs.mp4", "VR180 SBS")]
    [InlineData("SIVR-059.3d.180.sbs.mp4", "VR180 SBS")]
    [InlineData("SIVR-059 VR180 hsbs.mp4", "VR180 SBS")]
    [InlineData("SIVR-059_180_TB.mp4", "VR180 TB")]
    [InlineData("SIVR-059_180.mp4", "VR180")]
    [InlineData("SIVR-059_360_sbs.mp4", "VR360 SBS")]
    [InlineData("SIVR-059-VR360.mp4", "VR360")]
    [InlineData("SIVR-059_fisheye190.mp4", "Fisheye")]
    [InlineData("SIVR-059_fisheye_sbs.mp4", "Fisheye SBS")]
    [InlineData("SIVR-059_fisheye190_LR.mp4", "Fisheye SBS")]
    [InlineData("SIVR-059.fisheye.tb.mp4", "Fisheye TB")]
    [InlineData("SIVR-059_180_LR.mp4", "VR180 SBS")]
    [InlineData("SIVR-059_LR.mp4", null)]
    [InlineData("SIVR-059 [VR].mp4", "VR")]
    [InlineData("SIVR-059.vr.mp4", "VR")]
    // No format token: the code's own letters/digits never count.
    [InlineData("SIVR-059.mp4", null)]
    [InlineData("ABC-360min.mp4", null)]
    [InlineData("MIDE-400-RIFE-3.1.webm", null)]
    [InlineData(null, null)]
    public void FromFileName_FollowsJellyfinsTokensAndTheVrProjections(string? fileName, string? expected) =>
        Assert.Equal(expected, VrFormat.FromFileName(fileName, "SIVR-059"));

    [Theory]
    [InlineData("VRKM-180.mp4", "VRKM-180", null)]
    [InlineData("VRKM180_360_sbs.mp4", "VRKM-180", "VR360 SBS")]
    [InlineData("VRKM-180_180_sbs.mp4", "VRKM-180", "VR180 SBS")]
    [InlineData("other-name_180.mp4", "VRKM-180", "VR180")]
    public void FromFileName_IgnoresTheMoviesOwnCode(string fileName, string code, string? expected) =>
        Assert.Equal(expected, VrFormat.FromFileName(fileName, code));

    [Fact]
    public void Detect_ATaggedNameWins_ElseASideBySideFrameIsVr180Sbs_ElseTheVrGenre()
    {
        Assert.Equal("3D HTAB", VrFormat.Detect("X-1.3d.htab.mp4", "X-1", 3840, 1920, "VR Exclusive"));
        Assert.Equal("VR180 SBS", VrFormat.Detect("X-1.mp4", "X-1", 3840, 1920, null));
        Assert.Equal("VR180 SBS", VrFormat.Detect("X-1_180.mp4", "X-1", 7680, 3840, null));
        Assert.Equal("VR180", VrFormat.Detect("X-1_180.mp4", "X-1", 1920, 1080, null));
        Assert.Equal("Fisheye SBS", VrFormat.Detect("X-1_fisheye.mp4", "X-1", 3840, 1920, null));
        Assert.Equal("Fisheye TB", VrFormat.Detect("X-1_fisheye_tb.mp4", "X-1", 3840, 1920, null));
        Assert.Equal("Fisheye", VrFormat.Detect("X-1_fisheye.mp4", "X-1", 1920, 1080, null));
        Assert.Equal("VR", VrFormat.Detect("X-1.mp4", "X-1", 1920, 1080, "Solowork, VR Exclusive"));
        Assert.Null(VrFormat.Detect("X-1.mp4", "X-1", 1920, 1080, "Solowork, Big Tits"));
        Assert.Null(VrFormat.Detect("X-1.mp4", "X-1", null, null, null));
    }

    [Theory]
    [InlineData("VR", true)]
    [InlineData("Solowork, VR Exclusive", true)]
    [InlineData("High-Quality VR", true)]
    [InlineData("VRKM, Overtime", false)]
    [InlineData("Diverse", false)]
    [InlineData(null, false)]
    public void HasVrGenre_MatchesVrAsAWord(string? genres, bool expected) =>
        Assert.Equal(expected, VrFormat.HasVrGenre(genres));

    [Theory]
    [InlineData("VR180 SBS", "VR")]
    [InlineData("Fisheye", "VR")]
    [InlineData("Fisheye SBS", "VR")]
    [InlineData("VR", "VR")]
    [InlineData("3D HSBS", "3D")]
    [InlineData("3D", "3D")]
    [InlineData(null, null)]
    public void BadgeText_Is3DForLayoutsAndVrOtherwise(string? vrType, string? expected) =>
        Assert.Equal(expected, VrFormat.BadgeText(vrType));

    [Theory]
    [InlineData("RIFE-3.1.3d.hsbs", "RIFE-3.1")]
    [InlineData("4K_180_sbs", "4K")]
    [InlineData("4K_180_SBS_RIFE", "4K_RIFE")]
    [InlineData("4K_fisheye190_LR", "4K")]
    [InlineData("3d.hsbs", null)]
    [InlineData("[VR]", null)]
    [InlineData("RIFE-3.1", "RIFE-3.1")]
    public void StripFormatTokens_LeavesTheRestOfTheTag(string tag, string? expected) =>
        Assert.Equal(expected, VrFormat.StripFormatTokens(tag));

    [Fact]
    public void EveryFormat_FitsTheColumn() =>
        Assert.All(VrFormat.All, f => Assert.True(f.Length <= 20));
}
