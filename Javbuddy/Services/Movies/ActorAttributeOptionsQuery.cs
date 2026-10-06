using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

/// <summary>An actress on a movie: her row plus that movie's release date, which decides her age.</summary>
public sealed class ActorAppearance
{
    public Actor Actor { get; init; } = null!;
    public DateTime? Release { get; init; }
}

/// <summary>The cup sizes and range bounds the Actress Attributes filter can offer for a set of appearances.
/// Scalar projections, not entity DISTINCTs: this runs on every filter click.</summary>
public static class ActorAttributeOptionsQuery
{
    public static IQueryable<ActorAppearance> ForMovies(AppDbContext db) =>
        db.MovieActors.Select(ma => new ActorAppearance { Actor = ma.Actor, Release = ma.Movie.MetaReleaseDate });

    public static async Task<ActorAttributeOptions> LoadAsync(IQueryable<ActorAppearance> appearances, DateOnly today, CancellationToken ct)
    {
        // The cup each actress had on the release date, as MovieFilterPredicates resolves it.
        var cupSizes = (await appearances
                .Select(a => a.Actor.CupSizePeriods
                    .Where(p => a.Release != null && p.EffectiveFrom <= a.Release)
                    .OrderByDescending(p => p.EffectiveFrom)
                    .Select(p => p.CupSize)
                    .FirstOrDefault() ?? a.Actor.CupSize)
                .Where(c => c != null && c.Trim() != "")
                .Select(c => c!.Trim().ToUpper())
                .Distinct()
                .ToListAsync(ct))
            .OrderBy(c => c.Length).ThenBy(c => c, StringComparer.Ordinal)
            .ToList();

        // One aggregate over the appearances (this runs on every filter click). Ages are exact ages on release,
        // the same arithmetic as MovieFilterPredicates, so the slider never offers an age nobody had.
        var todayDate = today.ToDateTime(TimeOnly.MinValue);
        // GroupBy(_ => 1) yields at most one row; reading it client-side avoids EF's "First without OrderBy" warning.
        var totals = (await appearances
            .Select(a => new
            {
                a.Actor.HeightCm,
                a.Actor.Bust,
                a.Actor.Waist,
                a.Actor.Hips,
                Age = a.Actor.BirthDate == null ? (int?)null :
                    (a.Release ?? todayDate).Year - a.Actor.BirthDate.Value.Year - (((a.Release ?? todayDate).Month < a.Actor.BirthDate.Value.Month || ((a.Release ?? todayDate).Month == a.Actor.BirthDate.Value.Month && (a.Release ?? todayDate).Day < a.Actor.BirthDate.Value.Day)) ? 1 : 0),
            })
            .GroupBy(_ => 1)
            .Select(g => new
            {
                HeightMin = g.Min(a => a.HeightCm),
                HeightMax = g.Max(a => a.HeightCm),
                BustMin = g.Min(a => a.Bust),
                BustMax = g.Max(a => a.Bust),
                WaistMin = g.Min(a => a.Waist),
                WaistMax = g.Max(a => a.Waist),
                HipsMin = g.Min(a => a.Hips),
                HipsMax = g.Max(a => a.Hips),
                AgeMin = g.Min(a => a.Age),
                AgeMax = g.Max(a => a.Age),
            })
            .ToListAsync(ct)).SingleOrDefault();

        return new ActorAttributeOptions
        {
            CupSizes = cupSizes,
            Height = Bounds(totals?.HeightMin, totals?.HeightMax),
            Bust = Bounds(totals?.BustMin, totals?.BustMax),
            Waist = Bounds(totals?.WaistMin, totals?.WaistMax),
            Hips = Bounds(totals?.HipsMin, totals?.HipsMax),
            Age = Bounds(totals?.AgeMin, totals?.AgeMax),
        };
    }

    private static RangeBounds? Bounds(int? min, int? max) => min is { } lo && max is { } hi ? new RangeBounds(lo, hi) : null;
}
