using Javbuddy.Models;
using Javbuddy.Services.Movies;

namespace Javbuddy.Services.MovieDiscovery;

/// <summary>Applies a Discover candidate's already-scraped data onto a newly-added Movie's Meta*
/// fields — used as a fallback when the normal ownership-check-then-metadata pipeline
/// (<see cref="IMovieAddService"/>) finds nothing. That's the common case for an upcoming/pre-order
/// title: javinizer-go and r18.dev only index a release once it's actually out, so there's nothing
/// for either to find yet, even though the studio's own site (and so this candidate) already has
/// cover art, a title, and a release date.</summary>
public static class DiscoveredCandidateMetadataMapper
{
    public static void Apply(Movie movie, DiscoveredMovieCandidate candidate)
    {
        movie.MetaTitle = candidate.Title;
        movie.MetaStudio = candidate.Studio;
        movie.MetaReleaseDate = candidate.ReleaseDate;
        movie.MetaCoverUrl = candidate.CoverImageUrl;
        // No distinct backdrop scraped from the studio site — same fallback MovieMetadataMapper
        // uses when javinizer-go itself has nothing better than the cover to offer.
        movie.MetaBackdropUrl = candidate.CoverImageUrl;
        movie.MetaActresses = candidate.ActressNames.Count > 0 ? string.Join(", ", candidate.ActressNames) : null;
        movie.MetaSourceName = candidate.SourceName;
        movie.MetaFetchedAt = DateTime.UtcNow;
    }
}
