using System.Text.Json.Serialization;

namespace Javbuddy.Services.Movies;

/// <summary>The Scenes / Highlights / Apexes filter: how many of each a movie has, as an inclusive range per kind.
/// Plain init properties with defaults, so cookies and saved DeoVR groups written before a member existed still deserialize.</summary>
public sealed record MovieCountSelection
{
    public static readonly MovieCountSelection Empty = new();

    public IntRange Scenes { get; init; } = IntRange.None;
    public IntRange Highlights { get; init; } = IntRange.None;
    public IntRange Apexes { get; init; } = IntRange.None;

    [JsonIgnore] public int Count => (Scenes.IsSet ? 1 : 0) + (Highlights.IsSet ? 1 : 0) + (Apexes.IsSet ? 1 : 0);

    [JsonIgnore] public bool IsEmpty => Count == 0;
}

/// <summary>The highest count any movie has per kind, as range bounds from 0; null when no movie has any of that kind,
/// so there is nothing to slide.</summary>
public sealed record MovieCountOptions
{
    public static readonly MovieCountOptions None = new();

    public RangeBounds? Scenes { get; init; }
    public RangeBounds? Highlights { get; init; }
    public RangeBounds? Apexes { get; init; }

    [JsonIgnore] public bool HasAny => Scenes is not null || Highlights is not null || Apexes is not null;

    public static MovieCountOptions FromMaximums(int maxScenes, int maxHighlights, int maxApexes) => new()
    {
        Scenes = Bounds(maxScenes),
        Highlights = Bounds(maxHighlights),
        Apexes = Bounds(maxApexes),
    };

    private static RangeBounds? Bounds(int max) => max > 0 ? new RangeBounds(0, max) : null;
}
