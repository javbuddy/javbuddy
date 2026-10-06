namespace Javbuddy.Services.Common;

// Replicates the CSS flex-wrap line-breaking used by ActorPhotos.razor.css's justified grid
// (fixed tile height, width distributed via flex-grow/flex-basis) so the number of rows - and
// therefore the exact pixel height - a set of photos will occupy can be computed for photos that
// aren't rendered in the DOM yet.
public static class JustifiedGridLayout
{
    public const double TileHeight = 220;
    public const double TileGap = 4;

    public static int CountRows(IEnumerable<double> aspectRatios, double containerWidth)
    {
        if (containerWidth <= 0) return 0;

        var rows = 0;
        var rowWidth = 0.0;
        foreach (var aspectRatio in aspectRatios)
        {
            var tileWidth = Math.Min(aspectRatio * TileHeight, containerWidth);
            var needed = rowWidth == 0 ? tileWidth : rowWidth + TileGap + tileWidth;
            if (needed > containerWidth && rowWidth > 0)
            {
                rows++;
                rowWidth = tileWidth;
            }
            else
            {
                rowWidth = needed;
            }
        }

        if (rowWidth > 0) rows++;
        return rows;
    }

    public static double CalculateHeight(IEnumerable<double> aspectRatios, double containerWidth) =>
        CountRows(aspectRatios, containerWidth) * (TileHeight + TileGap);
}
