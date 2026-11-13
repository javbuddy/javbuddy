namespace Javbuddy.Components.Pages.ActorDetailSections;

/// <summary>The dialog Actor Detail has open. They are mutually exclusive (each sits over the whole page), so one
/// value says which, and each dialog component takes <c>Show</c> from it.</summary>
public enum ActorDetailDialog
{
    None,
    PhotoUpload,
    Lightbox,
    Merge,
    NfoSync,
    MinnanoAvSearch,
    DeleteConfirm,
}
