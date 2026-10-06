using Javbuddy.Models;

namespace Javbuddy.Services.MovieDiscovery;

/// <summary>A raw candidate as returned by an <see cref="IStudioDiscoverySource"/> scan — deliberately
/// separate from the persisted <c>DiscoveredMovieCandidate</c> entity, the same way
/// ActorEnrichment's <c>ActorMetadataResult</c> is kept separate from the <c>Actor</c> entity: sources
/// have no DB knowledge, that's the aggregating service's job.</summary>
public record DiscoveredMovieItem(
    string Code,
    string Title,
    string Studio,
    string? CoverImageUrl,
    IReadOnlyList<string> GalleryImageUrls,
    IReadOnlyList<string> ActressNames,
    DateTime? ReleaseDate,
    bool IsUpcoming);

/// <summary>A studio's bundled logo: its app-relative URL and the image's intrinsic pixel size, which
/// Movies &gt; Discover renders as the <c>&lt;img&gt;</c>'s width/height so the browser reserves its
/// box before the file arrives.</summary>
public record StudioLogo(string Url, int Width, int Height);

public record MovieDiscoveryScanResult(int TotalFound, int NewCount, int AlreadyTrackedCount);

/// <summary>One actress credited on a discovered candidate, resolved against tracked Actors at
/// display time (see <see cref="IMovieDiscoveryService.GetCandidateSectionsAsync"/>) rather than
/// persisted, so a later-added/renamed actor still matches an existing candidate without a rescan.</summary>
public record DiscoveredMovieActress(string Name, int? ActorId, string? ActorDisplayName);

public record DiscoveredMovieCandidateView(DiscoveredMovieCandidate Candidate, IReadOnlyList<DiscoveredMovieActress> Actresses);

/// <summary>One studio's New candidates, grouped for Movies &gt; Discover's per-studio sections.
/// Sections are ordered freshest-first (see <see cref="IMovieDiscoveryService.GetCandidateSectionsAsync"/>);
/// <see cref="Candidates"/> within a section are newest-first, same as the flat list used to be.</summary>
public record DiscoveredStudioSection(
    string SourceName,
    string StudioDisplayName,
    StudioLogo? Logo,
    IReadOnlyList<DiscoveredMovieCandidateView> Candidates);
