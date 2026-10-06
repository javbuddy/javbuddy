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

/// <summary>Holds the last computed statistics for a few minutes. Singleton, because the per-root breakdown
/// walks the filesystem (see LibraryStatisticsService) and the service itself is scoped.</summary>
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

/// <summary>Aggregates counts across movies, scenes, highlights, apexes, actors and tags for the Statistics page
///. The per-root breakdown resolves each local movie's root on the filesystem (no stored
/// LocalRootPath column), so the result is cached briefly.</summary>
public class LibraryStatisticsService(IDbContextFactory<AppDbContext> dbFactory, ILocalLibraryClient localLibrary, LibraryStatisticsCache cache) : ILibraryStatisticsService
{
    public Task<LibraryStatistics> GetAsync(CancellationToken ct = default) =>
        cache.GetOrComputeAsync(() => ComputeAsync(ct), ct);

    private async Task<LibraryStatistics> ComputeAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var movies = await db.Movies.AsNoTracking()
            .Select(m => new
            {
                m.Code,
                m.Status,
                m.VrType,
                m.IsFavorite,
                m.LocalFileSizeBytes,
                m.MediaDurationSeconds,
                m.MediaHeight,
                m.MediaVideoCodec,
            })
            .ToListAsync(ct);

        var local = movies.Where(m => m.LocalFileSizeBytes != null).ToList();

        var roots = await localLibrary.GetRootPathsAsync(ct);
        var rootCounts = new Dictionary<string, (int Count, long Bytes)>();
        foreach (var m in local)
        {
            var root = await localLibrary.ResolveRootForCodeAsync(m.Code!, ct) ?? "(not found on disk)";
            var (c, b) = rootCounts.GetValueOrDefault(root);
            rootCounts[root] = (c + 1, b + m.LocalFileSizeBytes!.Value);
        }
        foreach (var r in roots)
            rootCounts.TryAdd(r, (0, 0));

        var byResolution = local
            .GroupBy(m => ResolutionBucket(m.MediaHeight))
            .Select(g => new StatRow(g.Key, g.Count()))
            .OrderByDescending(r => ResolutionOrder(r.Label))
            .ToList();

        var byCodec = local
            .GroupBy(m => string.IsNullOrWhiteSpace(m.MediaVideoCodec) ? "Unknown" : m.MediaVideoCodec!)
            .Select(g => new StatRow(g.Key, g.Count()))
            .OrderByDescending(r => r.Count)
            .ToList();

        var topTags = await db.MovieTags.AsNoTracking()
            .GroupBy(mt => mt.Tag.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .Take(10)
            .ToListAsync(ct);

        var topActors = await db.MovieActors.AsNoTracking()
            .GroupBy(ma => new { ma.Actor.FirstName, ma.Actor.LastName })
            .Select(g => new { g.Key.FirstName, g.Key.LastName, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .Take(10)
            .ToListAsync(ct);

        var addedDates = await db.MovieFiles.AsNoTracking()
            .Where(f => f.FileAddedAt != null)
            .Select(f => f.FileAddedAt!.Value)
            .ToListAsync(ct);
        var addedPerMonth = addedDates
            .GroupBy(d => new DateTime(d.Year, d.Month, 1))
            .OrderByDescending(g => g.Key)
            .Take(12)
            .OrderBy(g => g.Key)
            .Select(g => new StatRow(g.Key.ToString("yyyy-MM"), g.Count()))
            .ToList();

        return new LibraryStatistics(
            Movies: movies.Count,
            MoviesGot: movies.Count(m => m.Status == MovieStatus.Got),
            MoviesMissing: movies.Count(m => m.Status == MovieStatus.Missing),
            MoviesVr: movies.Count(m => !string.IsNullOrEmpty(m.VrType)),
            MoviesFavorite: movies.Count(m => m.IsFavorite),
            TotalBytes: local.Sum(m => m.LocalFileSizeBytes ?? 0),
            TotalRuntimeHours: local.Sum(m => m.MediaDurationSeconds ?? 0) / 3600.0,
            Actors: await db.Actors.CountAsync(ct),
            ActorsFavorite: await db.Actors.CountAsync(a => a.IsFavorite, ct),
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
