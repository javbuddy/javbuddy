using System.Text.RegularExpressions;
using Javbuddy.Services.Movies;

namespace Javbuddy.Services.DeoVr;

/// <summary>How DeoVR should project a video: its <c>screenType</c> and <c>stereoMode</c>.</summary>
public sealed record DeoVrProjectionInfo(string ScreenType, string StereoMode);

/// <summary>Maps a version's VR / 3D format (see VrFormat) to DeoVR's projection
///. The file name only picks a fisheye format's lens (MKX200, RF52), which VrFormat
/// doesn't tell apart.</summary>
public static partial class DeoVrProjection
{
    public const string Flat = "flat";
    public const string Dome = "dome";
    public const string Sphere = "sphere";
    public const string Fisheye = "fisheye";

    private static readonly Dictionary<string, string> LensFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MKX200"] = "mkx200",
        ["RF52"] = "rf52",
    };

    /// <summary>VR formats keep their projection and layout; plain VR (projection unknown) is dome
    /// side-by-side, the usual VR release format. 3D formats play flat in their layout (a bare "3D" as
    /// side-by-side); MVC and flat video play flat in 2D.</summary>
    public static DeoVrProjectionInfo FromVrType(string? vrType, string? fileName) => vrType switch
    {
        VrFormat.Vr180Sbs => new(Dome, "sbs"),
        VrFormat.Vr180Tb => new(Dome, "tb"),
        VrFormat.Vr180 => new(Dome, "off"),
        VrFormat.Vr360Sbs => new(Sphere, "sbs"),
        VrFormat.Vr360Tb => new(Sphere, "tb"),
        VrFormat.Vr360 => new(Sphere, "off"),
        VrFormat.FisheyeSbs => new(FisheyeLens(fileName), "sbs"),
        VrFormat.FisheyeTb => new(FisheyeLens(fileName), "tb"),
        VrFormat.Fisheye => new(FisheyeLens(fileName), "off"),
        VrFormat.Vr => new(Dome, "sbs"),
        VrFormat.ThreeDHsbs or VrFormat.ThreeDFsbs or VrFormat.ThreeDSbs or VrFormat.ThreeD => new(Flat, "sbs"),
        VrFormat.ThreeDHtab or VrFormat.ThreeDFtab or VrFormat.ThreeDTab => new(Flat, "tb"),
        _ => new(Flat, "off"),
    };

    private static string FisheyeLens(string? fileName) =>
        Tokens(fileName).Select(t => LensFlags.GetValueOrDefault(t)).FirstOrDefault(s => s is not null) ?? Fisheye;

    private static string[] Tokens(string? fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? [] : TokenSeparator().Split(Path.GetFileNameWithoutExtension(fileName));

    [GeneratedRegex(@"[\s._\-\[\]()]+")]
    private static partial Regex TokenSeparator();
}
