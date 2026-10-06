using System.Globalization;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Components.Shared;

public enum PlayerClipKind
{
    Scene,
    Highlight,
    Apex,
}

/// <summary>A scene, highlight or apex the Scenes wall's ClipPlayerModal plays. A
/// scene without an effective end has no EndSeconds and plays to the end of the file.</summary>
public sealed record PlayerClip(
    PlayerClipKind Kind,
    int Id,
    int MovieId,
    string Code,
    string DisplayTitle,
    double StartSeconds,
    double? EndSeconds,
    string? JellyfinItemId,
    string? JellyfinServerId,
    double? DurationSeconds)
{
    public static PlayerClip FromScene(SceneWallCard card) =>
        new(PlayerClipKind.Scene, card.SceneId, card.MovieId, card.Code, card.DisplayTitle, card.StartSeconds, card.EffectiveEndSeconds,
            card.JellyfinItemId, card.JellyfinServerId, card.DurationSeconds);

    public static PlayerClip FromHighlight(HighlightWallCard card) =>
        new(PlayerClipKind.Highlight, card.HighlightId, card.MovieId, card.Code, card.DisplayTitle, card.StartSeconds, card.EndSeconds,
            card.JellyfinItemId, card.JellyfinServerId, card.DurationSeconds);

    /// <summary>The few seconds around an apex, see ApexRanges.ClipFor.</summary>
    public static PlayerClip FromApex(ApexWallCard card)
    {
        var (start, end) = ApexRanges.ClipFor(card.Seconds, card.Window, card.DurationSeconds);
        return new(PlayerClipKind.Apex, card.ApexId, card.MovieId, card.Code, card.DisplayTitle, start, end,
            card.JellyfinItemId, card.JellyfinServerId, card.DurationSeconds);
    }

    /// <summary>Movie Detail's deep link, which opens its player with the editor; an
    /// apex has none of its own, so its clip's start time (?t=) stands in.</summary>
    public string EditUrl => Kind switch
    {
        PlayerClipKind.Apex => $"/movies/{Uri.EscapeDataString(Code)}?t={StartSeconds.ToString("0.###", CultureInfo.InvariantCulture)}",
        PlayerClipKind.Highlight => $"/movies/{Uri.EscapeDataString(Code)}?highlight={Id}",
        _ => $"/movies/{Uri.EscapeDataString(Code)}?scene={Id}",
    };
}
