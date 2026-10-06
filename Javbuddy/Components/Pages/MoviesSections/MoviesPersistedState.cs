using Javbuddy.Models;
using Javbuddy.Services.Movies;

namespace Javbuddy.Components.Pages.MoviesSections;

/// <summary>Bridges prerendering's already-fetched first window into the interactive circuit that
/// reconnects right after it, so that reconnect doesn't have to re-query the DB (and briefly show
/// LoadingIndicator again, flashing over the grid the user can already see) before it can render
/// anything. Only ever travels from the prerendered response to that circuit, so its shape can
/// change freely.
///
/// The actor filter options aren't included: the actor list grows with the library, and the
/// browser hands the persisted state back in a single SignalR message capped at
/// MaximumReceiveMessageSize (Program.cs) — a few thousand actors pushed it over and killed every
/// circuit.</summary>
public sealed record MoviesPersistedState(
    List<MoviesPersistedState.PersistedMovie> Visible,
    int WindowStart,
    int FilteredCount,
    int TotalCount,
    int MissingCount,
    int GotCount,
    int FilesCount,
    long TotalFileSizeBytes,
    List<string> LibraryNames,
    List<string> DownloadingCodes,
    List<string> AvailableCodecs,
    List<string> AvailableStudios,
    List<string> AvailableGenres,
    ActorAttributeOptions ActorAttributeOptions,
    MoviesViewState Filters,
    int RandomSortSeed)
{
    public const string Key = "movies-initial-state";

    // A trimmed-down projection of just the fields the grid actually renders (see PosterCard's
    // usage in Movies.razor, MoviePosterOptions.MetaSubLines and MoviePosterText) — NOT the full
    // Movie entity. A page of 60 full Movie rows serializes to ~45KB, comfortably over SignalR's
    // default 32KB message-size limit, which silently kills the circuit the instant it tries to
    // reconnect with that much persisted state attached. The trimmed shape stays well under that
    // even for real (non-null) production data.
    public sealed record PersistedMovie(
        int Id,
        string? Code,
        MovieStatus Status,
        string? MetaSourceName,
        string? MetaTitle,
        string? Title,
        long? LocalFileSizeBytes,
        int? MediaWidth,
        int? MediaHeight,
        string? MediaScanType,
        int FileCount = 0,
        string? MetaActresses = null,
        DateTime? MetaFetchedAt = null,
        bool HasUnmatchedActors = false,
        string? UnmatchedActorNames = null,
        bool IsFavorite = false,
        string? VrType = null,
        string? PosterVersion = null)
    {
        public Movie ToMovie() => new()
        {
            Id = Id,
            Code = Code,
            Status = Status,
            MetaSourceName = MetaSourceName,
            MetaTitle = MetaTitle,
            Title = Title,
            LocalFileSizeBytes = LocalFileSizeBytes,
            MediaWidth = MediaWidth,
            MediaHeight = MediaHeight,
            MediaScanType = MediaScanType,
            FileCount = FileCount,
            MetaActresses = MetaActresses,
            MetaFetchedAt = MetaFetchedAt,
            HasUnmatchedActors = HasUnmatchedActors,
            UnmatchedActorNames = UnmatchedActorNames,
            IsFavorite = IsFavorite,
            VrType = VrType,
            PosterVersion = PosterVersion,
        };

        public static PersistedMovie FromMovie(Movie m) =>
            new(m.Id, m.Code, m.Status, m.MetaSourceName, m.MetaTitle, m.Title, m.LocalFileSizeBytes, m.MediaWidth, m.MediaHeight, m.MediaScanType, m.FileCount,
                m.MetaActresses, m.MetaFetchedAt, m.HasUnmatchedActors, m.UnmatchedActorNames, m.IsFavorite, m.VrType, m.PosterVersion);
    }
}
