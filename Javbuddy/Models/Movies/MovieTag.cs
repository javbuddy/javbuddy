namespace Javbuddy.Models;

/// <summary>Indexed association between a tracked movie and a canonical tag. A row is the movie's
/// effective tag when either flag is set: <see cref="IsExplicit"/> (metadata, hand-added) and/or
/// <see cref="FromClips"/> (carried by one of its scenes, highlights or apexes, kept in sync by
/// ClipTagSync). Everything that reads MovieTag sees both kinds; metadata ingestion
/// never turns a clip-only row explicit.</summary>
public class MovieTag
{
    public int MovieId { get; set; }
    public Movie Movie { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    // Deliberately no database default in the model (a bool default of true risks EF treating false as
    // "unset" on insert); the migration's column default only backfills rows that existed before it.
    public bool IsExplicit { get; set; } = true;

    public bool FromClips { get; set; }
}
