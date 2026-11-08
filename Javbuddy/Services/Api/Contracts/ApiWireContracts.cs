namespace Javbuddy.Services.Api.Contracts;

/// <summary>An absolute-link wire version of <see cref="MovieSummary"/>: the relative <c>webPath</c> plus <c>webUrl</c>, built from the request.</summary>
public sealed record ApiMovieSummary : MovieSummary
{
    public ApiMovieSummary(MovieSummary summary, string webUrl) : base(summary) => WebUrl = webUrl;
    public string WebUrl { get; }
}

public sealed record ApiMovieDetail : MovieDetail
{
    public ApiMovieDetail(MovieDetail detail, string webUrl) : base(detail) => WebUrl = webUrl;
    public string WebUrl { get; }
}

public sealed record ApiMovieAddResponse(ApiMovieSummary Movie, bool OwnedViaJellyfin, bool MetadataFound, string? MetadataError);

public sealed record ApiMetadataPatchResponse(ApiMovieDetail Movie, string? Warning);

public sealed record ApiReleaseGrabResponse(ApiMovieSummary Movie, bool Added, string? ReleaseTitle);

public sealed record ApiMovieListPage(int Total, IReadOnlyList<ApiMovieSummary> Items);

public sealed record ApiMovieLookupResult(string Query, string? Code, bool InLibrary, ApiMovieSummary? Movie);
