using System.Text.Json.Serialization;
using Javbuddy.Models;

namespace Javbuddy.Services.Movies;

/// <summary>The release year / runtime / bitrate / file size / cast size filters, as an inclusive range each. Bitrate is in
/// whole Mbps and file size in whole GiB, each value covering its whole unit (5 Mbps is 5000–5999 kbps). Plain init
/// properties with defaults, so cookies and saved DeoVR groups written before a member existed still deserialize.</summary>
public sealed record MovieRangeSelection
{
    public static readonly MovieRangeSelection Empty = new();

    public IntRange ReleaseYear { get; init; } = IntRange.None;
    public IntRange RuntimeMinutes { get; init; } = IntRange.None;
    public IntRange BitrateMbps { get; init; } = IntRange.None;
    public IntRange FileSizeGb { get; init; } = IntRange.None;
    public IntRange CastSize { get; init; } = IntRange.None;

    /// <summary>The ranges in the Filter menu's "Ranges" section; <see cref="CastSize"/> sits under "Actor".</summary>
    [JsonIgnore] public int MediaCount => (ReleaseYear.IsSet ? 1 : 0) + (RuntimeMinutes.IsSet ? 1 : 0) + (BitrateMbps.IsSet ? 1 : 0) + (FileSizeGb.IsSet ? 1 : 0);

    [JsonIgnore] public int Count => MediaCount + (CastSize.IsSet ? 1 : 0);

    [JsonIgnore] public bool IsEmpty => Count == 0;
}

/// <summary>What the movies offered span per range; null when none has that value, so there is nothing to slide.</summary>
public sealed record MovieRangeOptions
{
    public static readonly MovieRangeOptions None = new();

    public const long BytesPerGb = 1024L * 1024 * 1024;

    public RangeBounds? ReleaseYear { get; init; }
    public RangeBounds? RuntimeMinutes { get; init; }
    public RangeBounds? BitrateMbps { get; init; }
    public RangeBounds? FileSizeGb { get; init; }
    public RangeBounds? CastSize { get; init; }

    [JsonIgnore] public bool HasMedia => ReleaseYear is not null || RuntimeMinutes is not null || BitrateMbps is not null || FileSizeGb is not null;

    /// <summary>Bounds from each value's lowest and highest (bitrate in kbps, size in bytes); a range whose ends are equal
    /// has nothing to slide either. Cast size always starts at 0 (a movie with nobody linked).</summary>
    public static MovieRangeOptions FromExtremes(
        (int Min, int Max)? releaseYear, (int Min, int Max)? runtimeMinutes, (int Min, int Max)? bitrateKbps,
        (long Min, long Max)? fileSizeBytes, int maxCastSize) => new()
        {
            ReleaseYear = Bounds(releaseYear),
            RuntimeMinutes = Bounds(runtimeMinutes),
            BitrateMbps = Bounds(bitrateKbps is { } b ? (b.Min / 1000, b.Max / 1000) : null),
            FileSizeGb = Bounds(fileSizeBytes is { } s ? ((int)(s.Min / BytesPerGb), (int)(s.Max / BytesPerGb)) : null),
            CastSize = maxCastSize > 0 ? new RangeBounds(0, maxCastSize) : null,
        };

    /// <summary>The same bounds worked out in memory over loaded movies (Actor Detail's), which need MovieActors loaded.</summary>
    public static MovieRangeOptions FromMovies(IReadOnlyCollection<Movie> movies) => FromExtremes(
        Extremes(movies.Where(m => m.MetaReleaseDate != null).Select(m => m.MetaReleaseDate!.Value.Year)),
        Extremes(movies.Where(m => m.MetaRuntimeMinutes != null).Select(m => m.MetaRuntimeMinutes!.Value)),
        Extremes(movies.Select(m => m.MediaVideoBitRateKbps ?? m.MediaOverallBitRateKbps).OfType<int>()),
        Extremes(movies.Where(m => m.LocalFileSizeBytes != null).Select(m => m.LocalFileSizeBytes!.Value)),
        movies.Select(m => m.MovieActors.Count).DefaultIfEmpty().Max());

    private static (T Min, T Max)? Extremes<T>(IEnumerable<T> values) where T : struct, IComparable<T>
    {
        var list = values.ToList();
        return list.Count > 0 ? (list.Min(), list.Max()) : null;
    }

    private static RangeBounds? Bounds((int Min, int Max)? extremes) =>
        extremes is { } e && e.Min < e.Max ? new RangeBounds(e.Min, e.Max) : null;
}
