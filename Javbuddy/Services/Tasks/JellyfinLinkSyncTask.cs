using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tasks;

/// <summary>Checks every movie that isn't yet linked to a Jellyfin library item (regardless of
/// how it was added — local library scan, javinizer-go, manual) and links it (marking it Got)
/// if a match turns up in the selected libraries. The unattended version of the "Scan Jellyfin"
/// button on the Movies page and "Lookup in Jellyfin" on a movie's own page.</summary>
public class JellyfinLinkSyncTask(
    IJellyfinClient jellyfinClient,
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration,
    MovieChangeNotifier? movieChangeNotifier = null) : IScheduledTask
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(15);

    private readonly IJellyfinClient jellyfinClient = jellyfinClient;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly IConfiguration configuration = configuration;
    private readonly MovieChangeNotifier? movieChangeNotifier = movieChangeNotifier;

    public string Name => "Jellyfin Link Sync";

    public string Description =>
        "Links movies and actors not yet matched to Jellyfin library items — the unattended " +
        "version of the \"Scan Jellyfin\" button.";

    public TimeSpan GetInterval()
    {
        var configured = configuration["Jellyfin:LinkSyncIntervalHours"];
        if (double.TryParse(configured, out var hours) && hours > 0)
        {
            var interval = TimeSpan.FromHours(hours);
            return interval < MinimumInterval ? MinimumInterval : interval;
        }
        return DefaultInterval;
    }

    public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
    {
        if (!await jellyfinClient.IsEnabledAsync(ct))
        {
            return "skipped — Jellyfin integration is disabled";
        }

        var libraryNames = await jellyfinClient.GetSelectedLibraryNamesAsync(ct);
        if (libraryNames.Count == 0)
        {
            return "skipped — no libraries selected";
        }

        // Only Id + Code are needed to look each movie up; matches are reloaded and saved in
        // their own short-lived context below.
        List<LinkTarget> targets;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            targets = await db.Movies
                .Where(m => m.Code != null && m.Code != "" && m.JellyfinItemId == null)
                .Select(m => new LinkTarget(m.Id, m.Code!))
                .ToListAsync(ct);
        }

        var checkedCount = 0;
        var matched = 0;

        foreach (var movie in targets)
        {
            if (ct.IsCancellationRequested) break;
            checkedCount++;
            progress.Report(new TaskProgress(checkedCount, targets.Count, "Checking Jellyfin"));

            var result = await jellyfinClient.LookupInSelectedLibrariesAsync(movie.Code, ct);
            var match = result.Success ? result.Items?.FirstOrDefault() : null;
            if (match is null) continue;

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var tracked = await db.Movies.FindAsync(new object?[] { movie.Id }, ct);
            if (tracked is null) continue;

            JellyfinMetadataMapper.ApplyMatch(tracked, match);
            tracked.Status = MovieStatus.Got;
            await db.SaveChangesAsync(ct);
            matched++;
        }

        if (matched > 0)
        {
            movieChangeNotifier?.NotifyChanged();
        }

        List<Actor> unlinkedActors;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            unlinkedActors = await db.Actors
                .AsNoTracking()
                .Include(a => a.Aliases)
                .Where(a => a.JellyfinPersonId == null || a.JellyfinPersonId == "")
                .ToListAsync(ct);
        }

        var actorCheckedCount = 0;
        var actorMatched = 0;

        foreach (var actor in unlinkedActors)
        {
            if (ct.IsCancellationRequested) break;
            actorCheckedCount++;
            progress.Report(new TaskProgress(actorCheckedCount, unlinkedActors.Count, "Checking Jellyfin"));

            var candidateNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(actor.DisplayName))
            {
                candidateNames.Add(actor.DisplayName);
            }
            if (!string.IsNullOrWhiteSpace(actor.FirstName) && !string.IsNullOrWhiteSpace(actor.LastName))
            {
                var western = $"{actor.FirstName} {actor.LastName}".Trim();
                if (!candidateNames.Contains(western, StringComparer.OrdinalIgnoreCase))
                {
                    candidateNames.Add(western);
                }
            }
            foreach (var alias in actor.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias.Name) && !candidateNames.Contains(alias.Name, StringComparer.OrdinalIgnoreCase))
                {
                    candidateNames.Add(alias.Name);
                }
            }
            if (!string.IsNullOrWhiteSpace(actor.JapaneseNameKanji) && !candidateNames.Contains(actor.JapaneseNameKanji, StringComparer.OrdinalIgnoreCase))
            {
                candidateNames.Add(actor.JapaneseNameKanji);
            }

            string? foundPersonId = null;
            foreach (var candidate in candidateNames)
            {
                var person = await jellyfinClient.LookupPersonAsync(candidate, ct);
                if (person is not null && !string.IsNullOrWhiteSpace(person.Id))
                {
                    foundPersonId = person.Id;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(foundPersonId)) continue;

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var trackedActor = await db.Actors.FindAsync(new object?[] { actor.Id }, ct);
            if (trackedActor is null) continue;

            trackedActor.JellyfinPersonId = foundPersonId;
            await db.SaveChangesAsync(ct);
            actorMatched++;
        }

        // Domain-specific stats shown on the Connections page's Jellyfin card, in addition to
        // the generic ScheduledTaskRun history this task also gets from ScheduledTaskRunner.
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var settings = await db.JellyfinSettings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
            if (settings is null)
            {
                settings = new JellyfinSettings();
                db.JellyfinSettings.Add(settings);
            }
            settings.LastLinkSyncAt = DateTime.UtcNow;
            settings.LastLinkSyncChecked = checkedCount + actorCheckedCount;
            settings.LastLinkSyncMatched = matched + actorMatched;
            await db.SaveChangesAsync(ct);
        }

        if (actorCheckedCount > 0)
        {
            return $"checked {checkedCount + actorCheckedCount}, linked {matched + actorMatched} (movies: {matched}/{checkedCount}, actors: {actorMatched}/{actorCheckedCount})";
        }

        return $"checked {checkedCount}, linked {matched}";
    }

    private sealed record LinkTarget(int Id, string Code);
}
