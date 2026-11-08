namespace Javbuddy.Services.Api.Contracts;

/// <summary>A movie in a list or lookup. Field names are the public wire contract (camelCase on the wire).
/// <see cref="WebPath"/> is relative ("/movies/ABP-123"); the endpoint layer makes it an absolute <c>webUrl</c>.</summary>
public sealed record MovieSummary(
    int Id,
    string Code,
    string? Title,
    string Status,
    bool Favorite,
    string ContentType,
    DateOnly? ReleaseDate,
    string? Studio,
    int FileCount,
    string NfoDriftKind,
    string WebPath);

/// <summary>A tag on a movie. <see cref="Explicit"/> is a metadata or hand-added tag; <see cref="FromClips"/> a tag carried by one of its
/// scenes, highlights or apexes (both can be true).</summary>
public sealed record MovieTagInfo(int Id, string Name, bool Explicit, bool FromClips, string? Parent);

/// <summary>One name in the movie's cast. <see cref="ActorId"/> is set when it matches a tracked actor.</summary>
public sealed record MovieCastInfo(string Name, int? ActorId, bool Matched);

public sealed record MovieFileInfo(int Id, string FileName, string VersionTag, long SizeBytes, bool Primary, string? VrType);

public sealed record MovieNfoInfo(string DriftKind, string? Details);

/// <summary>Everything the API shows of a movie. <c>Title</c> is the fetched title, <c>TitleOverride</c> the manual one; runtime, rating
/// and cover URLs are read-only.</summary>
public sealed record MovieDetail(
    int Id,
    string Code,
    string? Title,
    string? TitleOverride,
    string? OriginalTitle,
    string? Description,
    DateOnly? ReleaseDate,
    string? Studio,
    string? Label,
    string? Series,
    string? Director,
    string ContentType,
    string Status,
    bool Favorite,
    int? RuntimeMinutes,
    double? Rating,
    int? RatingVotes,
    string? CoverUrl,
    string? BackdropUrl,
    string? VrType,
    IReadOnlyList<MovieTagInfo> Tags,
    IReadOnlyList<MovieCastInfo> Cast,
    IReadOnlyList<MovieFileInfo> Files,
    MovieNfoInfo Nfo,
    string WebPath);

/// <summary>One entry of a batch lookup: the query as given, and the library movie it resolved to (if any).</summary>
public sealed record MovieLookupResult(string Query, string? Code, bool InLibrary, MovieSummary? Movie);

/// <summary>A page of movies; <see cref="Total"/> counts every match, not just this page.</summary>
public sealed record MovieListPage(int Total, IReadOnlyList<MovieSummary> Items);

/// <summary>The filters of a movie list. Everything is optional; <see cref="Take"/> is capped at 200.</summary>
public sealed record MovieListQuery(
    string? Text = null,
    string? Status = null,
    string? Studio = null,
    string? Tag = null,
    int? ActorId = null,
    string? NfoDrift = null,
    int Skip = 0,
    int Take = 50);

public sealed record MovieAddResponse(MovieSummary Movie, bool OwnedViaJellyfin, bool MetadataFound, string? MetadataError);

public sealed record TagSearchItem(int Id, string Name, string? Parent);

public sealed record ActorSearchHit(int Id, string Name);
