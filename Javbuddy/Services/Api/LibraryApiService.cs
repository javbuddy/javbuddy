using System.Text.RegularExpressions;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Api.Contracts;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Api;

/// <summary>The one implementation of the public API's behavior, shared by the REST endpoints and the MCP tools: code
/// resolution, DTO mapping, validation and the calls into the existing services. Movies are addressed by code.</summary>
public interface ILibraryApiService
{
    /// <summary>Finds a library movie by a code as a client might write it. An exact, case-insensitive match wins; otherwise
    /// a zero-padding tolerant match on a plain letters-then-digits code (<see cref="CodeNormalization.GetZeroPaddingToleranceKey"/>:
    /// "abp123", "ABP-00123" and "abp-123" all find ABP-123). With several tolerant matches the shortest code wins.</summary>
    Task<Movie?> ResolveAsync(string code, CancellationToken ct = default);

    /// <summary>Resolves up to 100 codes at once (exact matches in one query, tolerant matches in a second).</summary>
    Task<ApiResult<IReadOnlyList<MovieLookupResult>>> LookupAsync(IReadOnlyList<string> codes, CancellationToken ct = default);

    Task<ApiResult<MovieListPage>> ListAsync(MovieListQuery query, CancellationToken ct = default);
    Task<ApiResult<MovieDetail>> GetDetailAsync(string code, CancellationToken ct = default);

    /// <summary>Adds the code as a Missing movie. A code already in the library (by <see cref="ResolveAsync"/>'s rules) is a
    /// Conflict carrying the existing movie.</summary>
    Task<ApiResult<MovieAddResponse>> AddAsync(string code, CancellationToken ct = default);

    Task<ApiResult<MetadataPatchResponse>> PatchMetadataAsync(string code, MetadataPatchRequest patch, CancellationToken ct = default);

    /// <summary>Adds an existing tag to the movie as an explicit tag. Adding one it already has is a no-op.</summary>
    Task<ApiResult<MovieDetail>> AddTagAsync(string code, int tagId, CancellationToken ct = default);

    /// <summary>Removes the movie's explicit tag (a tag it only carries through its clips stays). Removing one it hasn't is a no-op.</summary>
    Task<ApiResult<MovieDetail>> RemoveTagAsync(string code, int tagId, CancellationToken ct = default);

    Task<ApiResult<MovieDetail>> AddCastAsync(string code, int actorId, CancellationToken ct = default);
    Task<ApiResult<MovieDetail>> RemoveCastByActorAsync(string code, int actorId, CancellationToken ct = default);
    Task<ApiResult<MovieDetail>> RemoveCastByNameAsync(string code, string name, CancellationToken ct = default);

    Task<ApiResult<NfoPreview>> GetNfoAsync(string code, CancellationToken ct = default);
    Task<ApiResult<NfoPushResponse>> PushNfoAsync(string code, bool includeExternal, CancellationToken ct = default);
    Task<ApiResult<JavinizerPreview>> FetchJavinizerAsync(string code, CancellationToken ct = default);

    Task<IReadOnlyList<TagSearchItem>> SearchTagsAsync(string? query, CancellationToken ct = default);
    Task<IReadOnlyList<ActorSearchHit>> SearchActorsAsync(string query, CancellationToken ct = default);
    Task<SystemInfo> GetSystemInfoAsync(CancellationToken ct = default);
}

public sealed partial class LibraryApiService(
    IDbContextFactory<AppDbContext> dbFactory,
    IMovieGridQueryService gridQuery,
    IMovieDetailQueryService detailQuery,
    IMovieAddService addService,
    IMovieService movieService,
    ITagService tagService,
    IActorService actorService,
    INfoSyncService nfoSyncService,
    IConnectionStatusService connectionStatus) : ILibraryApiService
{
    public const int MaxLookupCodes = 100;
    public const int MaxPageSize = 200;
    private const int MaxSearchResults = 50;
    private const string JavinizerNotConfigured = "javinizer-go is not configured.";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_\-]{0,49}$")]
    private static partial Regex ValidCodeRegex();

    public async Task<Movie?> ResolveAsync(string code, CancellationToken ct = default)
    {
        var map = await ResolveManyAsync([code], ct);
        return map.Values.FirstOrDefault();
    }

    /// <summary>Query (trimmed) to the movie it resolved to; unresolved queries are absent.</summary>
    private async Task<Dictionary<string, Movie>> ResolveManyAsync(IEnumerable<string> queries, CancellationToken ct)
    {
        var wanted = queries.Select(q => q?.Trim() ?? string.Empty).Where(q => q.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new Dictionary<string, Movie>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return result;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Code is a NOCASE column, so IN matches case-insensitively and uses IX_Movies_Code.
        var exact = await db.Movies.AsNoTracking().Where(m => wanted.Contains(m.Code!)).ToListAsync(ct);
        foreach (var movie in exact)
        {
            result[movie.Code!] = movie;
        }

        var tolerant = wanted.Where(q => !result.ContainsKey(q))
            .Select(q => (Query: q, Key: CodeNormalization.GetZeroPaddingToleranceKey(q)))
            .Where(x => x.Key is not null)
            .ToList();
        if (tolerant.Count == 0) return result;

        var prefixes = tolerant.Select(x => x.Key![..x.Key!.IndexOf('-')]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var candidates = await db.Movies.AsNoTracking()
            .Where(m => prefixes.Any(p => m.Code!.StartsWith(p)))
            .ToListAsync(ct);
        var byKey = candidates
            .Select(m => (Movie: m, Key: CodeNormalization.GetZeroPaddingToleranceKey(m.Code)))
            .Where(x => x.Key is not null)
            .ToLookup(x => x.Key!, x => x.Movie, StringComparer.OrdinalIgnoreCase);
        foreach (var (query, key) in tolerant)
        {
            var match = byKey[key!].OrderBy(m => m.Code!.Length).ThenBy(m => m.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (match is not null)
            {
                result[query] = match;
            }
        }
        return result;
    }

    public async Task<ApiResult<IReadOnlyList<MovieLookupResult>>> LookupAsync(IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        if (codes.Count == 0)
        {
            return ApiResult<IReadOnlyList<MovieLookupResult>>.Invalid("Provide at least one code.");
        }
        if (codes.Count > MaxLookupCodes)
        {
            return ApiResult<IReadOnlyList<MovieLookupResult>>.Invalid($"At most {MaxLookupCodes} codes per lookup.");
        }

        var resolved = await ResolveManyAsync(codes, ct);
        IReadOnlyList<MovieLookupResult> results = codes.Select(q =>
        {
            var trimmed = q?.Trim() ?? string.Empty;
            return resolved.TryGetValue(trimmed, out var movie)
                ? new MovieLookupResult(q ?? string.Empty, movie.Code, true, MovieApiMapper.ToSummary(movie))
                : new MovieLookupResult(q ?? string.Empty, null, false, null);
        }).ToList();
        return ApiResult<IReadOnlyList<MovieLookupResult>>.Ok(results);
    }

    public async Task<ApiResult<MovieListPage>> ListAsync(MovieListQuery query, CancellationToken ct = default)
    {
        var errors = new List<string>();
        MovieStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (Enum.TryParse<MovieStatus>(query.Status.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) status = parsed;
            else errors.Add($"Invalid status '{query.Status}'. Use missing or got.");
        }
        IReadOnlyCollection<NfoDriftKind>? drift = null;
        if (!string.IsNullOrWhiteSpace(query.NfoDrift))
        {
            if (Enum.TryParse<NfoDriftKind>(query.NfoDrift.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) drift = [parsed];
            else errors.Add($"Invalid nfoDrift '{query.NfoDrift}'. Use one of: {string.Join(", ", Enum.GetValues<NfoDriftKind>().Select(MovieApiMapper.DriftKindName))}.");
        }
        if (errors.Count > 0)
        {
            return ApiResult<MovieListPage>.Invalid(errors);
        }

        var filter = new MovieGridFilter(
            Status: status,
            Text: string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim(),
            Studios: string.IsNullOrWhiteSpace(query.Studio) ? null : [query.Studio.Trim()],
            Genres: string.IsNullOrWhiteSpace(query.Tag) ? null : [query.Tag.Trim()],
            ActorIds: query.ActorId is { } actorId ? [actorId] : null,
            NfoDriftKinds: drift);
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, MaxPageSize);

        var total = await gridQuery.CountAsync(filter, ct);
        var movies = await gridQuery.GetRangeAsync(filter, new MovieGridSort("added", true, 0), skip, take, ct);
        return ApiResult<MovieListPage>.Ok(new MovieListPage(total, movies.Select(MovieApiMapper.ToSummary).ToList()));
    }

    public async Task<ApiResult<MovieDetail>> GetDetailAsync(string code, CancellationToken ct = default)
    {
        var movie = await ResolveAsync(code, ct);
        return movie is null ? NotFound<MovieDetail>(code) : ApiResult<MovieDetail>.Ok(await LoadDetailAsync(movie.Code!, ct));
    }

    /// <summary>The detail of a movie known to exist by its canonical code (re-read, so edits show).</summary>
    private async Task<MovieDetail> LoadDetailAsync(string canonicalCode, CancellationToken ct)
    {
        var movie = await detailQuery.GetByCodeAsync(canonicalCode, ct)
            ?? throw new InvalidOperationException($"Movie {canonicalCode} disappeared while being read.");
        var cast = await detailQuery.GetCastAsync(movie, ct);
        return MovieApiMapper.ToDetail(movie, movie.MovieTags, cast, movie.MovieFiles);
    }

    public async Task<ApiResult<MovieAddResponse>> AddAsync(string code, CancellationToken ct = default)
    {
        var trimmed = code?.Trim() ?? string.Empty;
        if (!ValidCodeRegex().IsMatch(trimmed))
        {
            return ApiResult<MovieAddResponse>.Invalid("Provide a movie code of up to 50 letters, digits, '-' or '_' (e.g. ABP-123).");
        }

        var existing = await ResolveAsync(trimmed, ct);
        if (existing is not null)
        {
            return Existing(existing);
        }

        MovieAddResult added;
        try
        {
            added = await addService.AddAsync(trimmed, ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with another add of the same code (unique NOCASE index).
            var winner = await ResolveAsync(trimmed, ct);
            if (winner is null) throw;
            return Existing(winner);
        }
        return ApiResult<MovieAddResponse>.Ok(new MovieAddResponse(MovieApiMapper.ToSummary(added.Movie), added.OwnedViaJellyfin, added.MetadataFound, added.MetadataError));

        static ApiResult<MovieAddResponse> Existing(Movie movie) => ApiResult<MovieAddResponse>.Conflict(
            $"{movie.Code} is already in the library.",
            new MovieAddResponse(MovieApiMapper.ToSummary(movie), false, true, null));
    }

    public async Task<ApiResult<MetadataPatchResponse>> PatchMetadataAsync(string code, MetadataPatchRequest patch, CancellationToken ct = default)
    {
        var movie = await ResolveAsync(code, ct);
        if (movie is null) return NotFound<MetadataPatchResponse>(code);

        var update = MovieMetadataPatch.Apply(movie, patch);
        if (!update.IsOk) return update.AsFailure<MetadataPatchResponse>();

        var saved = await movieService.UpdateMetadataAsync(movie.Id, update.Value!, ct);
        if (!saved.Success)
        {
            return ApiResult<MetadataPatchResponse>.Invalid(saved.ErrorMessage ?? "The metadata couldn't be saved.");
        }
        // A success with an error message is a warning: the edits are saved but e.g. a new .nfo couldn't be written.
        return ApiResult<MetadataPatchResponse>.Ok(new MetadataPatchResponse(await LoadDetailAsync(movie.Code!, ct), saved.ErrorMessage));
    }

    public async Task<ApiResult<MovieDetail>> AddTagAsync(string code, int tagId, CancellationToken ct = default)
    {
        var movie = await ResolveAsync(code, ct);
        if (movie is null) return NotFound<MovieDetail>(code);
        if (await tagService.GetByIdAsync(tagId, ct) is null) return ApiResult<MovieDetail>.NotFound($"Tag {tagId} not found.");

        var result = await tagService.AddTagToMovieAsync(movie.Id, tagId, ct);
        return result.Success
            ? ApiResult<MovieDetail>.Ok(await LoadDetailAsync(movie.Code!, ct))
            : ApiResult<MovieDetail>.Invalid(result.ErrorMessage ?? "The tag couldn't be added.");
    }

    public async Task<ApiResult<MovieDetail>> RemoveTagAsync(string code, int tagId, CancellationToken ct = default)
    {
        var movie = await ResolveAsync(code, ct);
        if (movie is null) return NotFound<MovieDetail>(code);

        var links = await detailQuery.GetMovieTagsAsync(movie.Id, ct);
        if (links.Any(l => l.TagId == tagId && l.IsExplicit))
        {
            // Not RemoveTagFromMovieAndClipsAsync, which would also strip the tag from the movie's scenes, highlights and apexes.
            var result = await tagService.RemoveTagFromMovieAsync(movie.Id, tagId, ct);
            if (!result.Success) return ApiResult<MovieDetail>.Invalid(result.ErrorMessage ?? "The tag couldn't be removed.");
        }
        return ApiResult<MovieDetail>.Ok(await LoadDetailAsync(movie.Code!, ct));
    }

    public async Task<ApiResult<MovieDetail>> AddCastAsync(string code, int actorId, CancellationToken ct = default)
    {
        var movie = await ResolveAsync(code, ct);
        if (movie is null) return NotFound<MovieDetail>(code);
        if (await actorService.GetByIdAsync(actorId, ct) is null) return ApiResult<MovieDetail>.NotFound($"Actor {actorId} not found.");

        var result = await movieService.AddActorToMovieAsync(movie.Id, actorId, ct);
        return result.Success
            ? ApiResult<MovieDetail>.Ok(await LoadDetailAsync(movie.Code!, ct))
            : ApiResult<MovieDetail>.Invalid(result.ErrorMessage ?? "The actor couldn't be added.");
    }

    public async Task<ApiResult<MovieDetail>> RemoveCastByActorAsync(string code, int actorId, CancellationToken ct = default)
    {
        var movie = await ResolveAsync(code, ct);
        if (movie is null) return NotFound<MovieDetail>(code);

        var result = await movieService.RemoveActorFromMovieAsync(movie.Id, actorId, ct);
        return result.Success
            ? ApiResult<MovieDetail>.Ok(await LoadDetailAsync(movie.Code!, ct))
            : ApiResult<MovieDetail>.Invalid(result.ErrorMessage ?? "The actor couldn't be removed.");
    }

    public async Task<ApiResult<MovieDetail>> RemoveCastByNameAsync(string code, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return ApiResult<MovieDetail>.Invalid("Provide the cast name to remove.");
        var movie = await ResolveAsync(code, ct);
        if (movie is null) return NotFound<MovieDetail>(code);

        var result = await movieService.RemoveActorFromMovieAsync(movie.Id, name.Trim(), ct);
        return result.Success
            ? ApiResult<MovieDetail>.Ok(await LoadDetailAsync(movie.Code!, ct))
            : ApiResult<MovieDetail>.Invalid(result.ErrorMessage ?? "The name couldn't be removed.");
    }

    public async Task<ApiResult<NfoPreview>> GetNfoAsync(string code, CancellationToken ct = default)
    {
        var movie = await ResolveAsync(code, ct);
        if (movie is null) return NotFound<NfoPreview>(code);

        var proposed = await nfoSyncService.GenerateProposedNfoAsync(movie.Id, ct);
        return ApiResult<NfoPreview>.Ok(new NfoPreview(MovieApiMapper.DriftKindName(movie.NfoDriftKind), movie.NfoConflictDetails, proposed));
    }

    public async Task<ApiResult<NfoPushResponse>> PushNfoAsync(string code, bool includeExternal, CancellationToken ct = default)
    {
        var movie = await ResolveAsync(code, ct);
        if (movie is null) return NotFound<NfoPushResponse>(code);

        var pushed = await nfoSyncService.PushNfoDriftAsync([movie.Id], includeExternal, ct: ct);
        return ApiResult<NfoPushResponse>.Ok(new NfoPushResponse(pushed.Written, pushed.SkippedExternal, pushed.Failed, pushed.StillDrifting));
    }

    public async Task<ApiResult<JavinizerPreview>> FetchJavinizerAsync(string code, CancellationToken ct = default)
    {
        var movie = await ResolveAsync(code, ct);
        if (movie is null) return NotFound<JavinizerPreview>(code);

        var fetched = await movieService.FetchJavinizerMetadataAsync(movie.Id, ct);
        if (!fetched.Success || fetched.Scraped is null)
        {
            var message = fetched.ErrorMessage ?? $"javinizer-go found no metadata for {movie.Code}.";
            return message == JavinizerNotConfigured
                ? ApiResult<JavinizerPreview>.NotConfigured(message)
                : ApiResult<JavinizerPreview>.Upstream(message);
        }

        var s = fetched.Scraped;
        return ApiResult<JavinizerPreview>.Ok(new JavinizerPreview(
            s.Id,
            s.Title,
            s.OriginalTitle,
            s.Description,
            s.ReleaseDate is { } date ? DateOnly.FromDateTime(date) : null,
            s.Maker,
            s.Label,
            s.Series,
            s.Director,
            s.Runtime,
            (s.Actresses ?? []).Select(a => string.Join(' ', new[] { a.FirstName, a.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)))).Where(n => n.Length > 0).ToList(),
            (s.Genres ?? []).Select(g => g.Name).OfType<string>().ToList(),
            s.CoverUrl));
    }

    public async Task<IReadOnlyList<TagSearchItem>> SearchTagsAsync(string? query, CancellationToken ct = default)
    {
        var tags = await tagService.GetTagsAsync(string.IsNullOrWhiteSpace(query) ? null : query.Trim(), ct: ct);
        return tags.Take(MaxSearchResults * 2).Select(t => new TagSearchItem(t.Id, t.Name, t.ParentTagName)).ToList();
    }

    public async Task<IReadOnlyList<ActorSearchHit>> SearchActorsAsync(string query, CancellationToken ct = default)
    {
        var actors = await actorService.SearchActorsAsync(query, ct);
        return actors.Take(MaxSearchResults).Select(a => new ActorSearchHit(a.Id, a.DisplayName)).ToList();
    }

    public async Task<SystemInfo> GetSystemInfoAsync(CancellationToken ct = default)
    {
        var statuses = (await connectionStatus.GetStatusesAsync(ct)).ToDictionary(s => s.Key, s => s.Configured);
        bool Configured(string key) => statuses.TryGetValue(key, out var configured) && configured;
        var version = typeof(LibraryApiService).Assembly.GetName().Version;
        var informational = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(LibraryApiService).Assembly)?.InformationalVersion;
        var text = informational?.Split('+')[0] ?? (version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}");
        return new SystemInfo(text, new IntegrationStatus(Configured("javinizer"), Configured("prowlarr"), Configured("qbittorrent"), Configured("jellyfin")));
    }

    private static ApiResult<T> NotFound<T>(string code) => ApiResult<T>.NotFound($"No movie with code '{code}' in the library.");
}
