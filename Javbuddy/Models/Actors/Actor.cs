using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

public class Actor
{
    public int Id { get; set; }

    public ICollection<MovieActor> MovieActors { get; } = new List<MovieActor>();
    public ICollection<ActorAlias> Aliases { get; } = new List<ActorAlias>();
    public ICollection<ActorCupSizePeriod> CupSizePeriods { get; } = new List<ActorCupSizePeriod>();

    [Required]
    [StringLength(200)]
    public string? FirstName { get; set; }

    [StringLength(200)]
    public string? LastName { get; set; }

    [StringLength(200)]
    public string? JapaneseNameKanji { get; set; }

    [StringLength(200)]
    public string? JapaneseNameKana { get; set; }

    [StringLength(200)]
    public string? JellyfinPersonId { get; set; }

    /// <summary>The Jellyfin server the person lives on, stored by the link sync so "Open in Jellyfin"
    /// is built from stored data without asking Jellyfin on a page load.</summary>
    [StringLength(200)]
    public string? JellyfinServerId { get; set; }

    public int? R18DevId { get; set; }

    /// <summary>Overrides the name used to look up this actor's filmography on r18.dev (Missing
    /// page / movie preview cast matching). r18.dev romanizes as "FirstName LastName", the
    /// opposite of the "LastName FirstName" convention DisplayName is normally in (e.g. javinizer-go's
    /// FormatActressName) — the automatic lookup already tries both orders for a two-word display name,
    /// but this is needed when that heuristic doesn't apply (more/fewer than two words, or a
    /// differently romanized name entirely).</summary>
    [StringLength(200)]
    public string? R18DevName { get; set; }

    public bool IsFavorite { get; set; }

    public DateTime? FavoritedAt { get; set; }

    /// <summary>Height in centimeters (e.g. 157).</summary>
    public int? HeightCm { get; set; }

    /// <summary>Japanese bra cup size letter (e.g. "C").</summary>
    [StringLength(10)]
    public string? CupSize { get; set; }

    /// <summary>Bust measurement in centimeters (e.g. 89).</summary>
    public int? Bust { get; set; }

    /// <summary>Waist measurement in centimeters (e.g. 65).</summary>
    public int? Waist { get; set; }

    /// <summary>Hip measurement in centimeters (e.g. 94).</summary>
    public int? Hips { get; set; }

    /// <summary>Birthdate of the actor.</summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>Indicates whether the actor has retired from the industry.</summary>
    public bool IsRetired { get; set; }

    /// <summary>Indicates whether the actor is transgender; the Actress Attributes filter and the Actors list filter on it.</summary>
    public bool IsTransgender { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set when this is an unidentified performer ("amateur") that exists only to carry actor tags and a
    /// photo within one movie: the movie it belongs to. Its FirstName is a label the user can change; its LastName is
    /// the movie's code, which they can't, and is shown in brackets after the label (see DisplayName). Such an actor is linked to that movie directly (never through
    /// the movie's cast text), is never written to the .nfo or synced to Jellyfin, is hidden from the actor list unless
    /// asked for, and goes away with the movie or when removed from it.</summary>
    public int? PlaceholderMovieId { get; set; }

    /// <summary>Whether this is an unidentified performer local to one movie (see <see cref="PlaceholderMovieId"/>).</summary>
    public bool IsPlaceholder => PlaceholderMovieId is not null;

    /// <summary>Whether the actor has a usable name that belongs in the .nfo. A first name is required (a one-name
    /// actor is stored as a first name only); a last name alone is not enough. An unidentified placeholder has no real
    /// name, so it never counts. The .nfo writers, the drift check and the actor sync all skip an actor without one,
    /// so they agree on who is written and compared.</summary>
    public bool HasName => !IsPlaceholder && !string.IsNullOrWhiteSpace(FirstName);

    /// <summary>LastName FirstName, matching the established cast-linking and route convention.</summary>
    public string DisplayName => IsPlaceholder
        ? Services.Actors.ActorDisplayName.Placeholder(FirstName ?? string.Empty, LastName)
        : Services.Actors.ActorDisplayName.Format(FirstName ?? string.Empty, LastName);

    /// <summary>FirstName LastName, the Western order (trimmed; empty when neither is set) — the name the same
    /// actor is credited under by sources that list her that way.</summary>
    public string WesternOrderName => Services.Actors.ActorDisplayName.WesternOrder(FirstName, LastName);

    public string? FormattedHeight => Services.Actors.ActorPhysicalAttributesHelper.FormatHeight(HeightCm);
    public string? FormattedMeasurements => Services.Actors.ActorPhysicalAttributesHelper.FormatMeasurements(Bust, Waist, Hips);
    public string? FormattedBirthDate => Services.Actors.ActorPhysicalAttributesHelper.FormatBirthDate(BirthDate);
    public bool HasPhysicalAttributes => HeightCm.HasValue || !string.IsNullOrWhiteSpace(CupSize) || CupSizePeriods.Count > 0 || Bust.HasValue || Waist.HasValue || Hips.HasValue || BirthDate.HasValue;
}
