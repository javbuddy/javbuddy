namespace Javbuddy.Models;

/// <summary>Which side changed a movie's drifting .nfo fields since the last time Javbuddy and
/// the .nfo agreed on them — see NfoDriftDetector. Declared in severity order: a movie with
/// several drifting fields is stored as the most severe one (BothChanged over ExternalEdit over
/// JavbuddyChanged). Unreadable means the .nfo couldn't be read or parsed at all.</summary>
public enum NfoDriftKind
{
    None,
    JavbuddyChanged,
    ExternalEdit,
    BothChanged,
    Unreadable,
}
