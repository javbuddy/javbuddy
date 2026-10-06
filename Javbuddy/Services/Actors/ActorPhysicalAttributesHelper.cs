using System.Globalization;
using System.Text.RegularExpressions;
using Javbuddy.Models;

namespace Javbuddy.Services.Actors;

public static partial class ActorPhysicalAttributesHelper
{
    public const string JapaneseCupSizeExplanation =
        "Japanese cup sizes differ from European standards: they do not indicate the volume of breasts only, but the global volume of breasts and a japanese padded bra. For example, a japanese C cup will frequently correspond to a B (sometimes A) European cup size.";

    public static readonly IReadOnlyList<string> StandardCupSizes =
        ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K"];

    public static bool IsStandardCupSize(string? cupSize) =>
        cupSize is not null && StandardCupSizes.Contains(cupSize, StringComparer.OrdinalIgnoreCase);

    // Metadata imports, enrichment and merges only store a standard cup size; anything else is treated as unknown.
    public static string? StandardCupSizeOrNull(string? cupSize)
    {
        var normalized = NormalizeCupSize(cupSize);
        return IsStandardCupSize(normalized) ? normalized : null;
    }

    // Actor Edit's cup size options: the standard sizes, plus the actor's stored value when it's a
    // non-standard one, so an existing selection survives until the user changes it.
    public static IReadOnlyList<string> CupSizeOptions(string? currentCupSize)
    {
        var current = NormalizeCupSize(currentCupSize);
        return current is null || IsStandardCupSize(current) ? StandardCupSizes : [.. StandardCupSizes, current];
    }

    public static int? CalculateAge(DateTime? birthDate, DateOnly? asOf = null)
    {
        if (!birthDate.HasValue)
        {
            return null;
        }

        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var birth = DateOnly.FromDateTime(birthDate.Value);

        var age = today.Year - birth.Year;
        if (today.Month < birth.Month || (today.Month == birth.Month && today.Day < birth.Day))
        {
            age--;
        }

        return age >= 0 ? age : null;
    }

    public const int MinimumActorAge = 18;

    // A birthdate is allowed when it's unknown or makes the actor at least MinimumActorAge today, on
    // the same "today" basis as CalculateAge so the cut-off agrees with the displayed age.
    public static bool IsAllowedBirthDate(DateTime? birthDate, DateOnly? asOf = null) =>
        !birthDate.HasValue || CalculateAge(birthDate, asOf) >= MinimumActorAge;

    // Latest birthdate IsAllowedBirthDate accepts, for the Actor Edit date picker's max.
    public static DateOnly LatestAllowedBirthDate(DateOnly? asOf = null) =>
        (asOf ?? DateOnly.FromDateTime(DateTime.UtcNow)).AddYears(-MinimumActorAge);

    // Earliest date a dated cup size may start: the actor's 18th birthday, or null without a birthdate.
    public static DateOnly? EarliestCupSizePeriodDate(DateTime? birthDate) =>
        birthDate.HasValue ? DateOnly.FromDateTime(birthDate.Value).AddYears(MinimumActorAge) : null;

    // Actor Detail's cup size over time, on the same rule as the movie filters: the regular
    // cup size until the first dated period, then each period until the next one starts. Consecutive steps with
    // the same cup are merged; the last step is the current one, since periods can't start after today.
    public static IReadOnlyList<CupSizeStep> CupSizeHistory(string? baseCupSize, IEnumerable<ActorCupSizePeriod> periods)
    {
        var starts = new List<(DateOnly? From, string Cup)>();
        if (NormalizeCupSize(baseCupSize) is { } baseCup)
        {
            starts.Add((null, baseCup));
        }

        foreach (var period in periods.OrderBy(p => p.EffectiveFrom))
        {
            if (NormalizeCupSize(period.CupSize) is not { } cup) continue;
            if (starts.Count > 0 && starts[^1].Cup == cup) continue;
            starts.Add((DateOnly.FromDateTime(period.EffectiveFrom), cup));
        }

        return starts
            .Select((start, i) => new CupSizeStep(start.Cup, start.From,
                i + 1 < starts.Count ? starts[i + 1].From?.AddDays(-1) : null))
            .ToList();
    }

    // The step's date range for its tooltip, in FormatBirthDate's dd.MM.yyyy style.
    public static string FormatCupSizeStepRange(CupSizeStep step)
    {
        static string Format(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        return (step.From, step.Until) switch
        {
            ({ } from, { } until) => $"{Format(from)} – {Format(until)}",
            (null, { } until) => $"until {Format(until)}",
            ({ } from, null) => $"from {Format(from)} (current)",
            _ => "current",
        };
    }

    // Age on the movie's release date, falling back to current age when the release date is unknown.
    public static int? CalculateAgeAtRelease(DateTime? birthDate, DateTime? releaseDate) =>
        CalculateAge(birthDate, releaseDate.HasValue ? DateOnly.FromDateTime(releaseDate.Value) : null);

    // Same "today" basis as CalculateAge, so the birthday indicator and the displayed age roll over together.
    // Feb 29 birthdays are celebrated on Feb 28 in non-leap years.
    public static bool IsBirthday(DateTime? birthDate, DateOnly? asOf = null)
    {
        if (!birthDate.HasValue)
        {
            return false;
        }

        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var birth = birthDate.Value;

        if (birth.Month == 2 && birth.Day == 29 && !DateTime.IsLeapYear(today.Year))
        {
            return today.Month == 2 && today.Day == 28;
        }

        return today.Month == birth.Month && today.Day == birth.Day;
    }

    public static string? FormatBirthDate(DateTime? birthDate, DateOnly? asOf = null)
    {
        if (!birthDate.HasValue)
        {
            return null;
        }

        var dateStr = birthDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var age = CalculateAge(birthDate, asOf);
        return age.HasValue ? $"{dateStr} ({age.Value})" : dateStr;
    }

    public static string? FormatHeight(int? heightCm)
    {
        if (!heightCm.HasValue || heightCm.Value <= 0)
        {
            return null;
        }

        return $"{heightCm.Value} cm";
    }

    public static string? FormatMeasurements(int? bust, int? waist, int? hips)
    {
        var b = bust is > 0 ? bust : null;
        var w = waist is > 0 ? waist : null;
        var h = hips is > 0 ? hips : null;

        if (b.HasValue && w.HasValue && h.HasValue)
        {
            return $"{b.Value}-{w.Value}-{h.Value}";
        }

        if (b.HasValue && w.HasValue)
        {
            return $"{b.Value}-{w.Value}";
        }

        if (b.HasValue)
        {
            return $"{b.Value}";
        }

        return null;
    }

    public static string? NormalizeCupSize(string? cupSize)
    {
        if (string.IsNullOrWhiteSpace(cupSize) || cupSize.Trim().Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return cupSize.Trim().ToUpperInvariant();
    }

    [GeneratedRegex(@"\b(?:\D*(\d{2,3})\D+(\d{2,3})\D+(\d{2,3}))\b")]
    private static partial Regex ThreeMeasurementsRegex();

    public static bool TryParseMeasurements(string? raw, out int? bust, out int? waist, out int? hips)
    {
        bust = null;
        waist = null;
        hips = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var match = ThreeMeasurementsRegex().Match(raw);
        if (match.Success
            && int.TryParse(match.Groups[1].Value, out var b)
            && int.TryParse(match.Groups[2].Value, out var w)
            && int.TryParse(match.Groups[3].Value, out var h))
        {
            bust = b;
            waist = w;
            hips = h;
            return true;
        }

        return false;
    }
}

/// <summary>One cup size an actor had, from <see cref="From"/> through <see cref="Until"/> (both inclusive; null
/// means since before the first dated period / still current), see <see cref="ActorPhysicalAttributesHelper.CupSizeHistory"/>.</summary>
public sealed record CupSizeStep(string CupSize, DateOnly? From, DateOnly? Until);
