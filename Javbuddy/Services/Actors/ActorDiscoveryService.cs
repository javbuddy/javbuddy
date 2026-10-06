using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Actors;

public record ActorDiscoveryResult(
    int Found,
    int AlreadyTracked,
    int Added,
    int ActorsScanned,
    int ImagesFound,
    IReadOnlyList<string> DiscoveredNames);

public record ActorScanResult(
    int TotalFound,
    int AlreadyTracked,
    IReadOnlyList<DiscoveredActorCandidate> Candidates);

public record ActorImportResult(
    int ImportedCount,
    int ImagesCached,
    IReadOnlyList<string> ImportedNames,
    IReadOnlyList<ActorImportFailure> Failures,
    IReadOnlyList<ActorImageImportFailure>? ImageFailures = null);

public record ActorImportFailure(string ActorName, string Reason);

public record ActorImageImportFailure(string ActorName, string ImagePath, string Reason);

public enum ActorDiscoverySource
{
    LocalLibrary
}

public interface IActorDiscoveryService
{
    Task<ActorScanResult> ScanFromMoviesAsync(IProgress<TaskProgress>? progress = null, CancellationToken ct = default);
    Task<ActorScanResult> ScanFromMoviesAsync(CancellationToken ct) => ScanFromMoviesAsync(null, ct);

    Task<ActorScanResult> ScanAsync(ActorDiscoverySource source = ActorDiscoverySource.LocalLibrary, IProgress<TaskProgress>? progress = null, CancellationToken ct = default)
        => source == ActorDiscoverySource.LocalLibrary ? ScanFromMoviesAsync(progress, ct) : throw new NotSupportedException($"Discovery source '{source}' is not supported yet.");
    Task<ActorScanResult> ScanAsync(ActorDiscoverySource source, CancellationToken ct) => ScanAsync(source, null, ct);

    Task<ActorImportResult> ImportCandidatesAsync(IReadOnlyList<DiscoveredActorCandidate> candidates, IProgress<TaskProgress>? progress = null, CancellationToken ct = default);
    Task<ActorImportResult> ImportCandidatesAsync(IReadOnlyList<DiscoveredActorCandidate> candidates, CancellationToken ct) => ImportCandidatesAsync(candidates, null, ct);

    Task<ActorDiscoveryResult> DiscoverFromMoviesAsync(IProgress<TaskProgress>? progress = null, CancellationToken ct = default);
    Task<ActorDiscoveryResult> DiscoverFromMoviesAsync(CancellationToken ct) => DiscoverFromMoviesAsync(null, ct);
}

public class DiscoveredActorCandidate
{
    public string Key { get; set; } = Guid.NewGuid().ToString("N");
    public string RawName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string DisplayName => ActorDisplayName.Format(FirstName, LastName);
    public string? LatinName { get; set; }
    public string? JapaneseKanji { get; set; }
    public string? JapaneseKana { get; set; }
    public string? Thumb { get; set; }
    public string? ThumbnailDataUrl { get; set; }
    public bool HasLocalHeadshot { get; set; }
    public string? LocalHeadshotMovieCode { get; set; }
    public bool HasImage => !string.IsNullOrWhiteSpace(ThumbnailDataUrl) || HasLocalHeadshot || !string.IsNullOrWhiteSpace(Thumb);
    public HashSet<string> MovieCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Source { get; set; } = "Local (.nfo)";

    public int MovieCount => MovieCodes.Count;

    public static DiscoveredActorCandidate FromMetadata(LocalActorMetadata meta, string? movieCode)
    {
        var candidate = new DiscoveredActorCandidate
        {
            RawName = meta.Name,
            Thumb = meta.Thumb,
            Source = "Local (.nfo)"
        };

        if (!string.IsNullOrWhiteSpace(movieCode)) candidate.MovieCodes.Add(movieCode);

        candidate.ClassifyName(meta.Name);
        if (!string.IsNullOrWhiteSpace(meta.AltName)) candidate.ClassifyName(meta.AltName);

        candidate.ComputeNames();
        return candidate;
    }

    public static DiscoveredActorCandidate FromName(string name, string? movieCode)
    {
        var candidate = new DiscoveredActorCandidate
        {
            RawName = name,
            Source = "Local (cast)"
        };

        if (!string.IsNullOrWhiteSpace(movieCode)) candidate.MovieCodes.Add(movieCode);
        candidate.ClassifyName(name);
        candidate.ComputeNames();
        return candidate;
    }

    public void MergeFrom(DiscoveredActorCandidate other)
    {
        foreach (var code in other.MovieCodes) MovieCodes.Add(code);
        Thumb ??= other.Thumb;
        ThumbnailDataUrl ??= other.ThumbnailDataUrl;
        LocalHeadshotMovieCode ??= other.LocalHeadshotMovieCode;
        HasLocalHeadshot = HasLocalHeadshot || other.HasLocalHeadshot;
        if (Source == "Local (cast)" && other.Source == "Local (.nfo)")
        {
            Source = other.Source;
        }

        if (string.IsNullOrWhiteSpace(LatinName) && !string.IsNullOrWhiteSpace(other.LatinName))
        {
            LatinName = other.LatinName;
            FirstName = other.FirstName;
            LastName = other.LastName;
        }

        if (string.IsNullOrWhiteSpace(JapaneseKanji) && !string.IsNullOrWhiteSpace(other.JapaneseKanji))
        {
            JapaneseKanji = other.JapaneseKanji;
        }

        if (string.IsNullOrWhiteSpace(JapaneseKana) && !string.IsNullOrWhiteSpace(other.JapaneseKana))
        {
            JapaneseKana = other.JapaneseKana;
        }
    }

    private void ClassifyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var trimmed = name.Trim();
        if (JapaneseTextHelper.HasKanji(trimmed))
        {
            JapaneseKanji ??= trimmed;
        }
        else if (JapaneseTextHelper.IsPureKana(trimmed))
        {
            JapaneseKana ??= trimmed;
        }
        else if (!JapaneseTextHelper.HasJapaneseCharacters(trimmed))
        {
            LatinName ??= trimmed;
        }
    }

    public void ComputeNames()
    {
        if (!string.IsNullOrWhiteSpace(LatinName))
        {
            (FirstName, LastName) = ActorDisplayName.Parse(LatinName);
        }
        else
        {
            var primary = JapaneseKanji ?? JapaneseKana ?? RawName;
            var parts = primary.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                FirstName = parts[1];
                LastName = parts[0];
            }
            else
            {
                FirstName = primary;
                LastName = null;
            }
        }
    }
}

public class ActorDiscoveryService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IActorImageCacheService imageCacheService,
    ILogger<ActorDiscoveryService>? logger = null) : IActorDiscoveryService
{
    public Task<ActorScanResult> ScanAsync(ActorDiscoverySource source, CancellationToken ct) => ScanAsync(source, null, ct);

    public Task<ActorScanResult> ScanAsync(ActorDiscoverySource source = ActorDiscoverySource.LocalLibrary, IProgress<TaskProgress>? progress = null, CancellationToken ct = default)
    {
        return source switch
        {
            ActorDiscoverySource.LocalLibrary => ScanFromMoviesAsync(progress, ct),
            _ => throw new NotSupportedException($"Discovery source '{source}' is not supported yet.")
        };
    }

    public Task<ActorScanResult> ScanFromMoviesAsync(CancellationToken ct) => ScanFromMoviesAsync(null, ct);

    public async Task<ActorScanResult> ScanFromMoviesAsync(IProgress<TaskProgress>? progress = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Only the code and cast summary are read below — don't materialize every Movie column.
        var movies = await db.Movies
            .Select(movie => new { movie.Code, movie.MetaActresses })
            .ToListAsync(ct);
        var existingActors = await db.Actors.Include(a => a.Aliases).ToListAsync(ct);

        // 1. Gather all candidates across all movies (.nfo files first, then MetaActresses string fallback)
        var candidates = new List<DiscoveredActorCandidate>();
        var totalMovies = movies.Count;
        progress?.Report(new TaskProgress(0, totalMovies, "Scanning movies"));

        for (var i = 0; i < totalMovies; i++)
        {
            ct.ThrowIfCancellationRequested();
            var movie = movies[i];
            progress?.Report(new TaskProgress(i + 1, totalMovies, "Scanning movies"));
            await Task.Yield();

            var movieActors = movie.Code is not null
                ? await localLibraryClient.GetMovieActorsAsync(movie.Code, ct)
                : [];

            var processedNamesInMovie = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var actorMeta in movieActors)
            {
                var candidate = DiscoveredActorCandidate.FromMetadata(actorMeta, movie.Code);
                candidates.Add(candidate);
                processedNamesInMovie.Add(candidate.DisplayName);
                processedNamesInMovie.Add(candidate.RawName);
                if (!string.IsNullOrWhiteSpace(candidate.LatinName)) processedNamesInMovie.Add(candidate.LatinName);
                if (!string.IsNullOrWhiteSpace(candidate.JapaneseKanji)) processedNamesInMovie.Add(candidate.JapaneseKanji);
                if (!string.IsNullOrWhiteSpace(candidate.JapaneseKana)) processedNamesInMovie.Add(candidate.JapaneseKana);
            }

            // Also check movie.MetaActresses for any names not in .nfo
            foreach (var name in ActorMatching.SplitNames(movie.MetaActresses))
            {
                if (!processedNamesInMovie.Contains(name))
                {
                    candidates.Add(DiscoveredActorCandidate.FromName(name, movie.Code));
                    processedNamesInMovie.Add(name);
                }
            }
        }

        ct.ThrowIfCancellationRequested();
        progress?.Report(new TaskProgress(0, null, "Consolidating candidates"));

        // 2. Consolidate candidates that share any name
        var consolidatedCandidates = ConsolidateCandidates(candidates);

        var alreadyTracked = 0;
        var pendingCandidates = new List<DiscoveredActorCandidate>();
        var existingEnriched = false;

        var totalCandidates = consolidatedCandidates.Count;
        progress?.Report(new TaskProgress(0, totalCandidates, "Resolving headshots"));

        // 3. Process each candidate against existing actors
        for (var i = 0; i < totalCandidates; i++)
        {
            ct.ThrowIfCancellationRequested();
            var candidate = consolidatedCandidates[i];
            progress?.Report(new TaskProgress(i + 1, totalCandidates, "Resolving headshots"));
            await Task.Yield();

            var matchedActor = existingActors.FirstOrDefault(a => CandidateMatchesActor(candidate, a));
            if (matchedActor is not null)
            {
                alreadyTracked++;

                var changed = false;
                if (string.IsNullOrWhiteSpace(matchedActor.JapaneseNameKanji) && !string.IsNullOrWhiteSpace(candidate.JapaneseKanji))
                {
                    matchedActor.JapaneseNameKanji = candidate.JapaneseKanji;
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(matchedActor.JapaneseNameKana) && !string.IsNullOrWhiteSpace(candidate.JapaneseKana))
                {
                    matchedActor.JapaneseNameKana = candidate.JapaneseKana;
                    changed = true;
                }

                if (changed)
                {
                    existingEnriched = true;
                }

                continue;
            }

            // Check if headshot exists locally and record which movie had it
            var (hasLocalHeadshot, localMovieCode, localFilePath) = await FindLocalActorImageAsync(candidate, candidate.DisplayName, ct);
            candidate.HasLocalHeadshot = hasLocalHeadshot;
            candidate.LocalHeadshotMovieCode = localMovieCode;

            if (!string.IsNullOrWhiteSpace(localFilePath) && File.Exists(localFilePath))
            {
                try
                {
                    var webpBytes = await ImageConversionGate.RunAsync(() => ImageConverter.ConvertToWebP(localFilePath, maxEdge: 120), ct);
                    candidate.ThumbnailDataUrl = $"data:image/webp;base64,{Convert.ToBase64String(webpBytes)}";
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger?.LogDebug(ex, "Failed to generate thumbnail data URL for candidate {Name} from {Path}", candidate.DisplayName, localFilePath);
                }
            }

            pendingCandidates.Add(candidate);
        }

        ct.ThrowIfCancellationRequested();
        progress?.Report(new TaskProgress(0, null, "Saving candidate changes"));
        await db.SaveChangesAsync(ct);

        if (existingEnriched)
        {
            progress?.Report(new TaskProgress(0, null, "Synchronizing movie cast"));
            await MovieActorAssociation.SynchronizeAllAsync(db, ct);
            await db.SaveChangesAsync(ct);
        }

        progress?.Report(new TaskProgress(totalCandidates, totalCandidates, "Scan complete"));

        return new ActorScanResult(
            consolidatedCandidates.Count,
            alreadyTracked,
            pendingCandidates);
    }

    public Task<ActorImportResult> ImportCandidatesAsync(
        IReadOnlyList<DiscoveredActorCandidate> candidates,
        CancellationToken ct) => ImportCandidatesAsync(candidates, null, ct);

    public async Task<ActorImportResult> ImportCandidatesAsync(
        IReadOnlyList<DiscoveredActorCandidate> candidates,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0)
        {
            return new ActorImportResult(0, 0, [], []);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var initialExistingActors = await db.Actors.AsNoTracking().Include(a => a.Aliases).ToListAsync(ct);

        var addedCandidates = new List<(int ActorId, DiscoveredActorCandidate Candidate)>();
        var addedActorIds = new List<int>();
        var importedNames = new List<string>();
        var failures = new List<ActorImportFailure>();
        var imageFailures = new List<ActorImageImportFailure>();

        var totalCandidates = candidates.Count;
        progress?.Report(new TaskProgress(0, totalCandidates, "Importing actors"));

        try
        {
            for (var i = 0; i < totalCandidates; i++)
            {
                ct.ThrowIfCancellationRequested();
                var candidate = candidates[i];
                progress?.Report(new TaskProgress(i + 1, totalCandidates, "Importing actors"));
                await Task.Yield();

                // Double-check not already tracked prior to import
                if (initialExistingActors.Any(a => CandidateMatchesActor(candidate, a)))
                {
                    continue;
                }

                try
                {
                    var newActor = CreateActorFromCandidate(candidate);

                    await using var candidateDb = await dbFactory.CreateDbContextAsync(ct);
                    candidateDb.Actors.Add(newActor);
                    await candidateDb.SaveChangesAsync(ct);

                    addedCandidates.Add((newActor.Id, candidate));
                    addedActorIds.Add(newActor.Id);
                    importedNames.Add(newActor.DisplayName);
                }
                catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
                {
                    logger?.LogWarning(ex, "Actor discovery import rejected candidate {ActorName} due to a database constraint", candidate.DisplayName);
                    failures.Add(new ActorImportFailure(candidate.DisplayName, "An actor with the same first and last name already exists."));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger?.LogWarning(ex, "Actor discovery import failed for candidate {ActorName}", candidate.DisplayName);
                    failures.Add(new ActorImportFailure(candidate.DisplayName, ex.GetBaseException().Message));
                }
            }

            if (addedActorIds.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new TaskProgress(0, null, "Linking movies to actors"));
                await MovieActorAssociation.SynchronizeAllAsync(db, ct);
                await db.SaveChangesAsync(ct);
            }

            var imagesFound = 0;
            var totalImagesToProcess = addedCandidates.Count;
            progress?.Report(new TaskProgress(0, totalImagesToProcess, "Importing headshots"));

            for (var i = 0; i < totalImagesToProcess; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (actorId, candidate) = addedCandidates[i];
                progress?.Report(new TaskProgress(i + 1, totalImagesToProcess, "Importing headshots"));
                await Task.Yield();

                var localImage = await FindLocalActorImageAsync(candidate, candidate.DisplayName, ct);
                var hasLocalFile = candidate.HasLocalHeadshot || localImage.Found;
                if (hasLocalFile)
                {
                    try
                    {
                        var (thumb, _) = await imageCacheService.GetOrCreateBothAsync(actorId, ct);
                        if (thumb is not null) imagesFound++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        imageFailures.Add(new ActorImageImportFailure(
                            candidate.DisplayName,
                            localImage.LocalFilePath ?? candidate.LocalHeadshotMovieCode ?? "local .actors image",
                            ex.GetBaseException().Message));
                        logger?.LogWarning(ex, "Actor discovery could not import image for {ActorName}", candidate.DisplayName);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(candidate.Thumb)
                         && ActorImageUrlHelper.IsUsableRemoteActorImageUrl(candidate.Thumb))
                {
                    var (thumb, _) = await imageCacheService.DownloadAndCacheRemoteImageAsync(actorId, candidate.Thumb, ct);
                    if (thumb is not null) imagesFound++;
                }
            }

            progress?.Report(new TaskProgress(totalCandidates, totalCandidates, "Import complete"));

            return new ActorImportResult(addedActorIds.Count, imagesFound, importedNames, failures, imageFailures);
        }
        catch (OperationCanceledException)
        {
            if (addedActorIds.Count > 0)
            {
                try
                {
                    await using var cleanupDb = await dbFactory.CreateDbContextAsync(CancellationToken.None);
                    await MovieActorAssociation.SynchronizeAllAsync(cleanupDb, CancellationToken.None);
                    await cleanupDb.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to synchronize movie actor associations after cancelled import");
                }
            }
            throw;
        }
    }

    public Task<ActorDiscoveryResult> DiscoverFromMoviesAsync(CancellationToken ct) => DiscoverFromMoviesAsync(null, ct);

    public async Task<ActorDiscoveryResult> DiscoverFromMoviesAsync(
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
    {
        var scanResult = await ScanFromMoviesAsync(progress, ct);
        var importResult = await ImportCandidatesAsync(scanResult.Candidates, progress, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var allActorsCount = await db.Actors.CountAsync(ct);

        return new ActorDiscoveryResult(
            scanResult.TotalFound,
            scanResult.AlreadyTracked,
            importResult.ImportedCount,
            allActorsCount,
            importResult.ImagesCached,
            importResult.ImportedNames);
    }

    private async Task<(bool Found, string? MovieCode, string? LocalFilePath)> FindLocalActorImageAsync(DiscoveredActorCandidate candidate, string actorName, CancellationToken ct)
    {
        if (candidate.MovieCodes.Count == 0) return (false, null, null);

        var namesToCheck = new List<string> { actorName };
        if (!string.IsNullOrWhiteSpace(candidate.JapaneseKanji)) namesToCheck.Add(candidate.JapaneseKanji);
        if (!string.IsNullOrWhiteSpace(candidate.JapaneseKana)) namesToCheck.Add(candidate.JapaneseKana);
        if (!string.IsNullOrWhiteSpace(candidate.Thumb) &&
            !candidate.Thumb.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !candidate.Thumb.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            namesToCheck.Add(candidate.Thumb);
        }

        foreach (var code in candidate.MovieCodes)
        {
            var found = await localLibraryClient.ResolveActorImagePathAsync(code, namesToCheck, ct);
            if (found is not null) return (true, code, found);
        }

        return (false, null, null);
    }

    private static bool CandidateMatchesActor(DiscoveredActorCandidate candidate, Actor actor)
    {
        if (string.Equals(candidate.DisplayName, actor.DisplayName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(actor.LastName) && !string.IsNullOrWhiteSpace(candidate.LastName))
        {
            if (string.Equals(candidate.FirstName, actor.LastName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.LastName, actor.FirstName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        else if (!string.IsNullOrWhiteSpace(actor.LastName))
        {
            var reversedActor = $"{actor.FirstName} {actor.LastName}";
            if (string.Equals(candidate.DisplayName, reversedActor, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        else if (!string.IsNullOrWhiteSpace(candidate.LastName))
        {
            var reversedCandidate = $"{candidate.FirstName} {candidate.LastName}";
            if (string.Equals(reversedCandidate, actor.DisplayName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var candidateHasLatin = !string.IsNullOrWhiteSpace(candidate.LatinName);
        var actorHasLatin = !string.IsNullOrWhiteSpace(actor.FirstName) && !JapaneseTextHelper.HasJapaneseCharacters(actor.FirstName);
        if (candidateHasLatin && actorHasLatin)
        {
            foreach (var alias in actor.Aliases)
            {
                if (string.Equals(candidate.DisplayName, alias.Name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.RawName, alias.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        if (!string.IsNullOrWhiteSpace(actor.JapaneseNameKanji) && !string.IsNullOrWhiteSpace(candidate.JapaneseKanji))
        {
            var actorKanji = actor.JapaneseNameKanji.Replace(" ", "");
            var candidateKanji = candidate.JapaneseKanji.Replace(" ", "");
            if (string.Equals(actorKanji, candidateKanji, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(actor.JapaneseNameKana) && !string.IsNullOrWhiteSpace(candidate.JapaneseKana))
        {
            var actorKana = actor.JapaneseNameKana.Replace(" ", "");
            var candidateKana = candidate.JapaneseKana.Replace(" ", "");
            if (string.Equals(actorKana, candidateKana, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(actor.R18DevName))
        {
            if (string.Equals(candidate.DisplayName, actor.R18DevName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.RawName, actor.R18DevName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var alias in actor.Aliases)
        {
            if (string.Equals(candidate.DisplayName, alias.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.RawName, alias.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (!string.IsNullOrWhiteSpace(candidate.JapaneseKanji)
                && string.Equals(candidate.JapaneseKanji.Replace(" ", ""), alias.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static Actor CreateActorFromCandidate(DiscoveredActorCandidate candidate)
    {
        return new Actor
        {
            FirstName = candidate.FirstName,
            LastName = candidate.LastName,
            JapaneseNameKanji = candidate.JapaneseKanji,
            JapaneseNameKana = candidate.JapaneseKana
        };
    }

    private static bool CandidateMatchesCandidate(DiscoveredActorCandidate a, DiscoveredActorCandidate b)
    {
        if (string.Equals(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(a.LastName) && !string.IsNullOrWhiteSpace(b.LastName))
        {
            if (string.Equals(a.FirstName, b.LastName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.LastName, b.FirstName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        else if (!string.IsNullOrWhiteSpace(a.LastName))
        {
            var reversedA = $"{a.FirstName} {a.LastName}";
            if (string.Equals(b.DisplayName, reversedA, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        else if (!string.IsNullOrWhiteSpace(b.LastName))
        {
            var reversedB = $"{b.FirstName} {b.LastName}";
            if (string.Equals(a.DisplayName, reversedB, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var aHasLatin = !string.IsNullOrWhiteSpace(a.LatinName);
        var bHasLatin = !string.IsNullOrWhiteSpace(b.LatinName);
        if (aHasLatin && bHasLatin)
            return false;

        if (!string.IsNullOrWhiteSpace(a.JapaneseKanji) && !string.IsNullOrWhiteSpace(b.JapaneseKanji))
        {
            var aKanji = a.JapaneseKanji.Replace(" ", "");
            var bKanji = b.JapaneseKanji.Replace(" ", "");
            if (string.Equals(aKanji, bKanji, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(a.JapaneseKana) && !string.IsNullOrWhiteSpace(b.JapaneseKana))
        {
            var aKana = a.JapaneseKana.Replace(" ", "");
            var bKana = b.JapaneseKana.Replace(" ", "");
            if (string.Equals(aKana, bKana, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static List<DiscoveredActorCandidate> ConsolidateCandidates(List<DiscoveredActorCandidate> candidates)
    {
        var result = new List<DiscoveredActorCandidate>();

        foreach (var c in candidates)
        {
            var match = result.FirstOrDefault(r => CandidateMatchesCandidate(r, c));
            if (match is not null)
            {
                match.MergeFrom(c);
            }
            else
            {
                result.Add(c);
            }
        }

        return result;
    }
}
