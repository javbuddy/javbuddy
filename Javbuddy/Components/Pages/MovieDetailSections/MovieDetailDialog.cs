namespace Javbuddy.Components.Pages.MovieDetailSections;

/// <summary>The dialog Movie Detail has open. They are mutually exclusive (each sits over the whole page, and the
/// lightbox closes itself before it asks for the crop), so one value says which, and each dialog component takes
/// <c>Show</c> from it.</summary>
public enum MovieDetailDialog
{
    None,
    Search,
    PreviewSearch,
    PosterLightbox,
    VideoPlayer,
    CoverCrop,
    Stills,
    MetadataRefresh,
    MetadataEditor,
    MediaInfo,
    // The .nfo editor, plain or as the review of a drifted .nfo.
    NfoEditor,
    NfoConflictReview,
    VideoRepair,
    DeleteConfirm,
}
