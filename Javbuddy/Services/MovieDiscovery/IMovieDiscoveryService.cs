using Javbuddy.Services.Movies;
using Javbuddy.Services.Tasks;

namespace Javbuddy.Services.MovieDiscovery;

public interface IMovieDiscoveryService
{
    /// <summary>Candidates awaiting a decision, grouped one section per studio and ordered by that
    /// studio's freshest known release (undated-only studios last); each candidate has its credited
    /// actress name(s) resolved against tracked Actors.</summary>
    Task<IReadOnlyList<DiscoveredStudioSection>> GetCandidateSectionsAsync(CancellationToken ct = default);

    /// <summary>Runs every registered <see cref="IStudioDiscoverySource"/> and upserts the results —
    /// shared by the manual "Scan Now" button and the weekly <c>MovieDiscoveryScanTask</c>.</summary>
    Task<MovieDiscoveryScanResult> RunScanAsync(IProgress<TaskProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Adds the candidate to the library via <see cref="IMovieAddService"/> and marks it Added.</summary>
    Task<MovieAddResult> AddCandidateAsync(int candidateId, CancellationToken ct = default);

    Task DismissCandidateAsync(int candidateId, CancellationToken ct = default);
}
