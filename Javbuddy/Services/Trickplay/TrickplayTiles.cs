using System.Globalization;

namespace Javbuddy.Services.Trickplay;

/// <summary>CSS for showing the one trickplay thumbnail nearest a point in time, cut out
/// of its tile sheet — the same maths ScrubBar.razor.js does for its hover preview, used as the
/// scene screenshot fallback when no screenshot has been generated.</summary>
public static class TrickplayTiles
{
    public static string Style(TrickplayLayout trickplay, double seconds, int displayWidth)
    {
        var scale = (double)displayWidth / trickplay.Width;
        var thumbsPerSheet = trickplay.TileWidth * trickplay.TileHeight;
        var index = (int)Math.Clamp(Math.Floor(Math.Max(seconds - trickplay.StartSeconds, 0) * 1000 / trickplay.IntervalMs), 0, Math.Max(trickplay.ThumbnailCount - 1, 0));
        var sheet = index / thumbsPerSheet;
        var withinSheet = index % thumbsPerSheet;
        var column = withinSheet % trickplay.TileWidth;
        var row = withinSheet / trickplay.TileWidth;
        var url = trickplay.TileUrlTemplate.Replace("{index}", sheet.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        return FormattableString.Invariant(
            $"width:{displayWidth}px;height:{Math.Round(trickplay.Height * scale)}px;background-image:url(\"{url}\");background-size:{trickplay.TileWidth * trickplay.Width * scale:0.##}px {trickplay.TileHeight * trickplay.Height * scale:0.##}px;background-position:{-column * trickplay.Width * scale:0.##}px {-row * trickplay.Height * scale:0.##}px");
    }
}
