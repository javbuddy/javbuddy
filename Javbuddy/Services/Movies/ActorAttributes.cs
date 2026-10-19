using System.Text.Json.Serialization;

namespace Javbuddy.Services.Movies;

/// <summary>An inclusive range where either end may be open.</summary>
public sealed record IntRange(int? Min = null, int? Max = null)
{
    public static readonly IntRange None = new();

    [JsonIgnore] public bool IsSet => Min is not null || Max is not null;
}

public sealed record RangeBounds(int Min, int Max);

/// <summary>The Transgender criterion of the Actress Attributes filter. Saved in cookies as its integer value, so only ever append.</summary>
public enum TransgenderFilter
{
    Any,
    /// <summary>One linked actress who is transgender (and meets the other criteria).</summary>
    Only,
    /// <summary>No linked actress is transgender.</summary>
    Exclude,
}

/// <summary>The Actress Attributes filter: one actress must meet every criterion set. Plain
/// init properties with defaults, so cookies written before a member existed still deserialize.</summary>
public sealed record ActorAttributeSelection
{
    public static readonly ActorAttributeSelection Empty = new();

    public IReadOnlyList<string> CupSizes { get; init; } = [];
    public IntRange Height { get; init; } = IntRange.None;
    public IntRange Bust { get; init; } = IntRange.None;
    public IntRange Waist { get; init; } = IntRange.None;
    public IntRange Hips { get; init; } = IntRange.None;
    public IntRange Age { get; init; } = IntRange.None;
    public TransgenderFilter Transgender { get; init; } = TransgenderFilter.Any;

    [JsonIgnore] public int Count => CupSizes.Count + Ranges.Count(r => r.IsSet) + (Transgender != TransgenderFilter.Any ? 1 : 0);

    [JsonIgnore] public bool IsEmpty => Count == 0;

    [JsonIgnore] private IEnumerable<IntRange> Ranges => [Height, Bust, Waist, Hips, Age];

    /// <summary>Value equality, which the record's own <c>==</c> can't give because CupSizes is a list.</summary>
    public bool SameAs(ActorAttributeSelection other) =>
        CupSizes.SequenceEqual(other.CupSizes)
        && Height == other.Height && Bust == other.Bust && Waist == other.Waist && Hips == other.Hips && Age == other.Age
        && Transgender == other.Transgender;
}

/// <summary>What a view offers: cup sizes and a bound per range (null when nobody offered has that value).</summary>
public sealed record ActorAttributeOptions
{
    public static readonly ActorAttributeOptions None = new();

    public IReadOnlyList<string> CupSizes { get; init; } = [];
    public RangeBounds? Height { get; init; }
    public RangeBounds? Bust { get; init; }
    public RangeBounds? Waist { get; init; }
    public RangeBounds? Hips { get; init; }
    public RangeBounds? Age { get; init; }
    /// <summary>True when a transgender actress is among those offered, so the Transgender choice is worth showing.</summary>
    public bool Transgender { get; init; }

    [JsonIgnore] public bool HasAny => Transgender || CupSizes.Count > 0 || Height is not null || Bust is not null || Waist is not null || Hips is not null || Age is not null;
}
