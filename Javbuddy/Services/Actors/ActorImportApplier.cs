using Javbuddy.Data;
using Javbuddy.Models;

namespace Javbuddy.Services.Actors;

/// <summary>What an external actor source (WAPdB, minnano-av) offers, in the shape the import needs.</summary>
public sealed record ImportedActorData(
    int? HeightCm,
    string? CupSize,
    int? Bust,
    int? Waist,
    int? Hips,
    DateTime? BirthDate,
    bool? IsRetired,
    string? JapaneseNameKanji,
    string? JapaneseNameKana,
    IReadOnlyList<string> Aliases);

/// <summary>Which of an import's fields the user ticked.</summary>
public sealed record ImportSelection(
    bool JapaneseName, bool Height, bool CupSize, bool Measurements, bool BirthDate, bool RetiredStatus, bool Aliases);

/// <summary>The source-independent half of importing actor metadata: writing the ticked fields onto an
/// existing actor or onto a newly created one. Matching, naming a new actor and photos stay with the
/// source-specific import in <see cref="ActorService"/>.</summary>
public static class ActorImportApplier
{
    /// <summary>Overwrites the ticked fields that the source has a value for and adds any aliases the
    /// actor doesn't already have (nor as its display name). The caller saves.</summary>
    public static void ApplyToExisting(AppDbContext db, Actor actor, ImportedActorData data, ImportSelection selection)
    {
        if (selection.Height && data.HeightCm.HasValue) actor.HeightCm = data.HeightCm;
        if (selection.CupSize && ActorPhysicalAttributesHelper.StandardCupSizeOrNull(data.CupSize) is { } cupSize) actor.CupSize = cupSize;
        if (selection.Measurements)
        {
            if (data.Bust.HasValue) actor.Bust = data.Bust;
            if (data.Waist.HasValue) actor.Waist = data.Waist;
            if (data.Hips.HasValue) actor.Hips = data.Hips;
        }
        if (selection.BirthDate && data.BirthDate.HasValue && ActorPhysicalAttributesHelper.IsAllowedBirthDate(data.BirthDate)) actor.BirthDate = data.BirthDate;
        if (selection.RetiredStatus && data.IsRetired.HasValue) actor.IsRetired = data.IsRetired.Value;
        if (selection.JapaneseName && !string.IsNullOrWhiteSpace(data.JapaneseNameKanji))
        {
            actor.JapaneseNameKanji = data.JapaneseNameKanji.Trim();
            if (!string.IsNullOrWhiteSpace(data.JapaneseNameKana)) actor.JapaneseNameKana = data.JapaneseNameKana.Trim();
        }

        if (!selection.Aliases) return;
        foreach (var alias in data.Aliases)
        {
            var trimmed = alias.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) &&
                !actor.Aliases.Any(a => string.Equals(a.Name, trimmed, StringComparison.OrdinalIgnoreCase)) &&
                !string.Equals(actor.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                var newAlias = new ActorAlias { ActorId = actor.Id, Name = trimmed, CreatedAt = DateTime.UtcNow };
                actor.Aliases.Add(newAlias);
                db.ActorAliases.Add(newAlias);
            }
        }
    }

    /// <summary>A new, unsaved actor with the given names and only the ticked fields the source has.</summary>
    public static Actor CreateNew(string firstName, string? lastName, ImportedActorData data, ImportSelection selection)
    {
        var actor = new Actor
        {
            FirstName = firstName,
            LastName = lastName,
            JapaneseNameKanji = selection.JapaneseName && !string.IsNullOrWhiteSpace(data.JapaneseNameKanji) ? data.JapaneseNameKanji.Trim() : null,
            JapaneseNameKana = selection.JapaneseName && !string.IsNullOrWhiteSpace(data.JapaneseNameKana) ? data.JapaneseNameKana.Trim() : null,
            HeightCm = selection.Height && data.HeightCm.HasValue ? data.HeightCm : null,
            CupSize = selection.CupSize ? ActorPhysicalAttributesHelper.StandardCupSizeOrNull(data.CupSize) : null,
            Bust = selection.Measurements && data.Bust.HasValue ? data.Bust : null,
            Waist = selection.Measurements && data.Waist.HasValue ? data.Waist : null,
            Hips = selection.Measurements && data.Hips.HasValue ? data.Hips : null,
            BirthDate = selection.BirthDate && ActorPhysicalAttributesHelper.IsAllowedBirthDate(data.BirthDate) ? data.BirthDate : null,
            IsRetired = selection.RetiredStatus && data.IsRetired == true,
            CreatedAt = DateTime.UtcNow
        };

        if (selection.Aliases)
        {
            foreach (var alias in data.Aliases)
            {
                var trimmed = alias.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    actor.Aliases.Add(new ActorAlias { Name = trimmed, CreatedAt = DateTime.UtcNow });
                }
            }
        }

        return actor;
    }
}
