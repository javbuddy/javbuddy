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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>LastName FirstName, matching the established cast-linking and route convention.</summary>
    public string DisplayName => Services.Actors.ActorDisplayName.Format(FirstName ?? string.Empty, LastName);

    public string? FormattedHeight => Services.Actors.ActorPhysicalAttributesHelper.FormatHeight(HeightCm);
    public string? FormattedMeasurements => Services.Actors.ActorPhysicalAttributesHelper.FormatMeasurements(Bust, Waist, Hips);
    public string? FormattedBirthDate => Services.Actors.ActorPhysicalAttributesHelper.FormatBirthDate(BirthDate);
    public bool HasPhysicalAttributes => HeightCm.HasValue || !string.IsNullOrWhiteSpace(CupSize) || CupSizePeriods.Count > 0 || Bust.HasValue || Waist.HasValue || Hips.HasValue || BirthDate.HasValue;
}
