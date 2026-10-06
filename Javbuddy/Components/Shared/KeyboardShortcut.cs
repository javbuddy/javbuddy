namespace Javbuddy.Components.Shared;

/// <summary>One row of a ShortcutsInfo list: the keys (each drawn as a key cap,
/// alternatives separated by " / ") and what they do. The shared entries keep the wording the same
/// in every player and on the Review card.</summary>
public sealed record KeyboardShortcut(IReadOnlyList<string> Keys, string Action)
{
    public static readonly KeyboardShortcut PlayPause = new(["Space"], "Play / pause");
    public static readonly KeyboardShortcut Seek = new(["←", "→"], "Skip back / forward 5 s (with Shift: 60 s)");
    public static readonly KeyboardShortcut FrameStep = new([",", "."], "Step one frame back / forward (pauses)");
    public static readonly KeyboardShortcut Fullscreen = new(["F"], "Toggle fullscreen (or double-click the video)");
    public static readonly KeyboardShortcut NewScene = new(["M"], "New scene at the playhead");
    public static readonly KeyboardShortcut EndScene = new(["Shift+M"], "End the scene the playhead is in");
    public static readonly KeyboardShortcut StartHighlight = new(["H"], "Start a highlight at the playhead");
    public static readonly KeyboardShortcut EndHighlight = new(["Shift+H"], "End the started highlight at the playhead");
    public static readonly KeyboardShortcut MarkApex = new(["A"], "Open a new apex at the playhead");
    public static readonly KeyboardShortcut JumpHighlight = new(["H", "Shift+H"], "Jump to the next / previous highlight");
    public static readonly KeyboardShortcut JumpApex = new(["A", "Shift+A"], "Jump to the next / previous apex (5 s before it)");
}
