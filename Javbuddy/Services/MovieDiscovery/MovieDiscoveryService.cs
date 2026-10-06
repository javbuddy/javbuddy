using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Services.MovieDiscovery;

public class MovieDiscoveryService(
    IEnumerable<IStudioDiscoverySource> sources,
    IDbContextFactory<AppDbContext> dbFactory,
    IMovieAddService movieAddService,
    TimeProvider timeProvider,
    ILogger<MovieDiscoveryService>? logger = null) : IMovieDiscoveryService
{
    private readonly List<IStudioDiscoverySource> sources = sources.ToList();
    private readonly ILogger<MovieDiscoveryService> logger = logger ?? NullLogger<MovieDiscoveryService>.Instance;

    public async Task<IReadOnlyList<DiscoveredStudioSection>> GetCandidateSectionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Most recent/soonest activity first within each studio: dated candidates newest-first,
        // undated ones (a pre-order listing with no exact per-item date — see
        // UpTimelyHtmlParser.ParseReserveListing) last since there's nothing to sort them by. GroupBy
        // below preserves this relative order within each group, so it doesn't need re-sorting.
        var candidates = await db.DiscoveredMovieCandidates
            .Where(c => c.Status == DiscoveredMovieStatus.New)
            .OrderBy(c => c.ReleaseDate == null)
            .ThenByDescending(c => c.ReleaseDate)
            .ThenByDescending(c => c.FirstSeenAt)
            .AsNoTracking()
            .ToListAsync(ct);

        var actors = new ActorMatching.ActorIndex(await ActorMatching.LoadActorLookupsAsync(db, ct));
        var logoBySource = sources.ToDictionary(s => s.SourceName, s => s.Logo, StringComparer.OrdinalIgnoreCase);

        var views = candidates
            .Select(candidate => new DiscoveredMovieCandidateView(
                candidate,
                candidate.ActressNames
                    .Select(name =>
                    {
                        var match = actors.FindMatch(name);
                        return new DiscoveredMovieActress(name, match?.Id, match?.DisplayName);
                    })
                    .ToList()))
            .ToList();

        // Sections are ordered by each studio's own freshest known release date (a studio with
        // only undated/upcoming candidates sorts last, same nulls-last convention as the
        // per-candidate sort above), so whichever studio just dropped something new floats to the top.
        return views
            .GroupBy(v => v.Candidate.SourceName)
            .Select(group =>
            {
                var groupCandidates = (IReadOnlyList<DiscoveredMovieCandidateView>)group.ToList();
                var freshest = groupCandidates
                    .Where(v => v.Candidate.ReleaseDate is not null)
                    .Select(v => v.Candidate.ReleaseDate!.Value)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();
                var section = new DiscoveredStudioSection(
                    group.Key,
                    groupCandidates[0].Candidate.Studio,
                    logoBySource.GetValueOrDefault(group.Key),
                    groupCandidates);
                return (Section: section, Freshest: freshest);
            })
            .OrderByDescending(x => x.Freshest)
            .Select(x => x.Section)
            .ToList();
    }

    public async Task<MovieDiscoveryScanResult> RunScanAsync(IProgress<TaskProgress>? progress = null, CancellationToken ct = default)
    {
        var found = new List<(IStudioDiscoverySource Source, DiscoveredMovieItem Item)>();

        HashSet<string> disabled;
        await using (var settingsDb = await dbFactory.CreateDbContextAsync(ct))
        {
            disabled = await DiscoverySettingsService.DisabledSourceNamesAsync(settingsDb, ct);
        }
        var enabledSources = sources.Where(s => !disabled.Contains(s.SourceName)).ToList();

        for (var i = 0; i < enabledSources.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var source = enabledSources[i];
            progress?.Report(new TaskProgress(i, enabledSources.Count, $"Scanning {source.SourceName}"));

            try
            {
                var items = await source.ScanAsync(ct);
                found.AddRange(items.Select(item => (source, item)));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Discovery source {Source} failed", source.SourceName);
            }
        }

        progress?.Report(new TaskProgress(enabledSources.Count, enabledSources.Count, "Saving results"));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var trackedCodes = new HashSet<string>(
            await db.Movies.Select(m => m.Code!).ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        var existingCandidates = await db.DiscoveredMovieCandidates.ToDictionaryAsync(
            c => c.Code, c => c, StringComparer.OrdinalIgnoreCase, ct);

        var newCount = 0;
        var alreadyTrackedCount = 0;

        foreach (var (source, item) in found)
        {
            if (trackedCodes.Contains(item.Code))
            {
                alreadyTrackedCount++;
                if (existingCandidates.TryGetValue(item.Code, out var trackedCandidate)
                    && trackedCandidate.Status != DiscoveredMovieStatus.Added)
                {
                    trackedCandidate.Status = DiscoveredMovieStatus.Added;
                    trackedCandidate.LastSeenAt = now;
                }
                continue;
            }

            if (existingCandidates.TryGetValue(item.Code, out var candidate))
            {
                candidate.Title = item.Title;
                candidate.Studio = item.Studio;
                candidate.SourceName = source.SourceName;
                candidate.CoverImageUrl = item.CoverImageUrl;
                candidate.GalleryImageUrls = item.GalleryImageUrls.ToList();
                candidate.ActressNames = item.ActressNames.ToList();
                candidate.ReleaseDate = item.ReleaseDate;
                candidate.IsUpcoming = item.IsUpcoming;
                candidate.LastSeenAt = now;
                // Status is intentionally left alone: a Dismissed or Added candidate shouldn't
                // reappear as New just because a later scan saw the same code again.
            }
            else
            {
                db.DiscoveredMovieCandidates.Add(new DiscoveredMovieCandidate
                {
                    Code = item.Code,
                    Title = item.Title,
                    Studio = item.Studio,
                    SourceName = source.SourceName,
                    CoverImageUrl = item.CoverImageUrl,
                    GalleryImageUrls = item.GalleryImageUrls.ToList(),
                    ActressNames = item.ActressNames.ToList(),
                    ReleaseDate = item.ReleaseDate,
                    IsUpcoming = item.IsUpcoming,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    Status = DiscoveredMovieStatus.New,
                });
                newCount++;
            }
        }

        await db.SaveChangesAsync(ct);

        return new MovieDiscoveryScanResult(found.Count, newCount, alreadyTrackedCount);
    }

    public async Task<MovieAddResult> AddCandidateAsync(int candidateId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var candidate = await db.DiscoveredMovieCandidates.FirstOrDefaultAsync(c => c.Id == candidateId, ct)
            ?? throw new InvalidOperationException($"Discovered movie candidate {candidateId} not found.");

        var result = await movieAddService.AddAsync(candidate.Code, ct);

        // javinizer-go/r18.dev only index a release once it's actually out, so an upcoming/
        // pre-order title normally comes back with no metadata at all — fall back to what the
        // studio's own site (and so this candidate) already told us, rather than leaving the
        // newly-added Movie blank until a real provider catches up.
        if (!result.MetadataFound)
        {
            var tracked = await db.Movies.FindAsync([result.Movie.Id], ct);
            if (tracked is not null)
            {
                DiscoveredCandidateMetadataMapper.Apply(tracked, candidate);
                await MovieActorAssociation.SynchronizeAsync(db, tracked, ct);
            }
        }

        candidate.Status = DiscoveredMovieStatus.Added;
        candidate.LastSeenAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        return result;
    }

    public async Task DismissCandidateAsync(int candidateId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var candidate = await db.DiscoveredMovieCandidates.FirstOrDefaultAsync(c => c.Id == candidateId, ct)
            ?? throw new InvalidOperationException($"Discovered movie candidate {candidateId} not found.");

        candidate.Status = DiscoveredMovieStatus.Dismissed;
        await db.SaveChangesAsync(ct);
    }
}
