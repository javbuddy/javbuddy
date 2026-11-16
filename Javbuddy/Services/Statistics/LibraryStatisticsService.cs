using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Statistics;

/// <summary>One labelled count (and optionally a byte total) in a breakdown.</summary>
public record StatRow(string Label, int Count, long Bytes = 0);

public record LibraryStatistics(
    int Movies,
    int MoviesGot,
    int MoviesMissing,
    int MoviesVr,
    int MoviesFavorite,
    long TotalBytes,
    double TotalRuntimeHours,
    int Actors,
    int ActorsFavorite,
    int Tags,
    int TagsNeedingReview,
    int Scenes,
    int Highlights,
    int Apexes,
    IReadOnlyList<StatRow> ByRoot,
    IReadOnlyList<StatRow> ByResolution,
    IReadOnlyList<StatRow> ByCodec,
    IReadOnlyList<StatRow> TopTags,
    IReadOnlyList<StatRow> TopActors,
    IReadOnlyList<StatRow> AddedPerMonth);

public interface ILibraryStatisticsService
{
    Task<LibraryStatistics> GetAsync(CancellationToken ct = default);
}

/// <summary>Holds the last computed statistics for a few minutes. Singleton, because the service itself is scoped.</summary>
public class LibraryStatisticsCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim gate = new(1, 1);
    private LibraryStatistics? value;
    private DateTime computedAt;

    public async Task<LibraryStatistics> GetOrComputeAsync(Func<Task<LibraryStatistics>> compute, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (value is null || DateTime.UtcNow - computedAt > Ttl)
            {
                value = await compute();
                computedAt = DateTime.UtcNow;
            }
            return value;
        }
        finally
        {
            gate.Release();
        }
    }
}

/// <summary>Aggregates counts across movies, scenes, highlights, apexes, actors and tags for the Statistics page,
/// in SQL. The per-root breakdown reads Movie.LocalRootPath (no filesystem access), and the result is cached briefly.</summary>
public class LibraryStatisticsService(IDbContextFactory<AppDbContext> dbFactory, ILocalLibraryClient localLibrary, LibraryStatisticsCache cache) : ILibraryStatisticsService
{
    public Task<LibraryStatistics> GetAsync(CancellationToken ct = default) =>
        cache.GetOrComputeAsync(() => ComputeAsync(ct), ct);

    private async Task<LibraryStatistics> ComputeAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var movies = db.Movies.AsNoTracking();
        var local = movies.Where(m => m.LocalFileSizeBytes != null);

        // The root comes from the indexed Movie.LocalRootPath (filled in by a scan), not a filesystem lookup per
        // movie. A local movie that was never scanned since that column was added has none yet: "(unknown)".
        var rootGroups = await local
            .GroupBy(m => m.LocalRootPath ?? "")
            .Select(g => new { Root = g.Key, Count = g.Count(), Bytes = g.Sum(m => m.LocalFileSizeBytes!.Value) })
            .ToListAsync(ct);
        var rootCounts = new Dictionary<string, (int Count, long Bytes)>();
        foreach (var g in rootGroups)
        {
            var root = string.IsNullOrWhiteSpace(g.Root) ? UnknownRoot : g.Root;
            var (c, b) = rootCounts.GetValueOrDefault(root);
            rootCounts[root] = (c + g.Count, b + g.Bytes);
        }
        foreach (var r in await localLibrary.GetRootPathsAsync(ct))
            rootCounts.TryAdd(r, (0, 0));

        var byResolution = (await local
                .GroupBy(m => m.MediaHeight)
                .Select(g => new { Height = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .GroupBy(r => ResolutionBucket(r.Height))
            .Select(g => new StatRow(g.Key, g.Sum(r => r.Count)))
            .OrderByDescending(r => ResolutionOrder(r.Label))
            .ToList();

        var byCodec = (await local
                .GroupBy(m => m.MediaVideoCodec ?? "")
                .Select(g => new { Codec = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Codec) ? "Unknown" : r.Codec)
            .Select(g => new StatRow(g.Key, g.Sum(r => r.Count)))
            .OrderByDescending(r => r.Count)
            .ToList();

        var topTags = await db.MovieTags.AsNoTracking()
            .GroupBy(mt => mt.Tag.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .Take(10)
            .ToListAsync(ct);

        var topActors = await db.MovieActors.AsNoTracking()
            .Where(ma => ma.Actor.PlaceholderMovieId == null)
            .GroupBy(ma => new { ma.Actor.FirstName, ma.Actor.LastName })
            .Select(g => new { g.Key.FirstName, g.Key.LastName, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .Take(10)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var since = new DateTime(now.Year, now.Month, 1).AddMonths(-11);
        var until = since.AddMonths(12); // a file with a future creation time must not add a 13th month
        var addedPerMonth = (await db.MovieFiles.AsNoTracking()
                .Where(f => f.FileAddedAt != null && f.FileAddedAt >= since && f.FileAddedAt < until)
                .GroupBy(f => new { f.FileAddedAt!.Value.Year, f.FileAddedAt!.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync(ct))
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .Select(r => new StatRow(new DateTime(r.Year, r.Month, 1).ToString("yyyy-MM"), r.Count))
            .ToList();

        return new LibraryStatistics(
            Movies: await movies.CountAsync(ct),
            MoviesGot: await movies.CountAsync(m => m.Status == MovieStatus.Got, ct),
            MoviesMissing: await movies.CountAsync(m => m.Status == MovieStatus.Missing, ct),
            MoviesVr: await movies.CountAsync(m => m.VrType != null && m.VrType != "", ct),
            MoviesFavorite: await movies.CountAsync(m => m.IsFavorite, ct),
            TotalBytes: rootCounts.Values.Sum(v => v.Bytes),
            TotalRuntimeHours: await local.SumAsync(m => m.MediaDurationSeconds ?? 0, ct) / 3600.0,
            Actors: await db.Actors.CountAsync(a => a.PlaceholderMovieId == null, ct),
            ActorsFavorite: await db.Actors.CountAsync(a => a.PlaceholderMovieId == null && a.IsFavorite, ct),
            Tags: await db.Tags.CountAsync(ct),
            TagsNeedingReview: await db.Tags.CountAsync(t => t.NeedsReview, ct),
            Scenes: await db.Scenes.CountAsync(ct),
            Highlights: await db.MovieHighlights.CountAsync(ct),
            Apexes: await db.MovieApexes.CountAsync(ct),
            ByRoot: rootCounts.OrderByDescending(kv => kv.Value.Count)
                .Select(kv => new StatRow(kv.Key, kv.Value.Count, kv.Value.Bytes)).ToList(),
            ByResolution: byResolution,
            ByCodec: byCodec,
            TopTags: topTags.Select(t => new StatRow(t.Name, t.Count)).ToList(),
            TopActors: topActors
                .Select(a => new StatRow(Actors.ActorDisplayName.Format(a.FirstName ?? string.Empty, a.LastName), a.Count))
                .ToList(),
            AddedPerMonth: addedPerMonth);
    }

    private const string UnknownRoot = "(unknown)";

    public static string ResolutionBucket(int? height) => height switch
    {
        null => "Unknown",
        >= 4000 => "8K+",
        >= 2000 => "4K",
        >= 1000 => "1080p",
        >= 700 => "720p",
        _ => "SD",
    };

    private static int ResolutionOrder(string label) => label switch
    {
        "8K+" => 5,
        "4K" => 4,
        "1080p" => 3,
        "720p" => 2,
        "SD" => 1,
        _ => 0,
    };
}
