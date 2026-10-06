using System.Globalization;
using Javbuddy.Services.Scenes;

namespace Javbuddy.Components.Shared;

/// <summary>A scene, highlight or apex form's own actors: null while the clip inherits
/// them, else the chosen set, which replaces what it would inherit.</summary>
public interface IClipActorForm
{
    HashSet<int>? ActorIds { get; set; }
}

/// <summary>The scene form's actors, built from the scene each render; ClipEditor applies a change at once.</summary>
public sealed class SceneActorFormModel(IReadOnlyList<SceneActorItem> own) : IClipActorForm
{
    public HashSet<int>? ActorIds { get; set; } = own.Count > 0 ? [.. own.Select(a => a.ActorId)] : null;
}

/// <summary>ClipEditor's highlight add/edit form state, shared with ClipHighlightForm (the shortcuts set
/// its times from ClipEditor).</summary>
public sealed class HighlightFormModel : IClipActorForm
{
    public int? HighlightId { get; init; }
    public string Title { get; set; } = string.Empty;
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public List<SceneTagItem> Tags { get; set; } = [];
    public HashSet<int>? ActorIds { get; set; }
}

/// <summary>ClipEditor's apex add/edit form state, shared with ClipApexForm.</summary>
public sealed class ApexFormModel : IClipActorForm
{
    public int? ApexId { get; init; }
    public string Time { get; set; } = string.Empty;

    /// <summary>Its own lead-in and tail in seconds; blank uses the default.</summary>
    public string LeadIn { get; set; } = string.Empty;
    public string Tail { get; set; } = string.Empty;
    public List<SceneTagItem> Tags { get; set; } = [];
    public HashSet<int>? ActorIds { get; set; }
}

/// <summary>An apex form's lead-in/tail text: seconds, or blank for the default.</summary>
public static class ApexWindowText
{
    public static string Format(double? seconds) =>
        seconds is { } s ? s.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>False when the text is neither blank nor a number; blank parses as null.</summary>
    public static bool TryParse(string? text, out double? seconds)
    {
        seconds = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
        seconds = value;
        return true;
    }
}
