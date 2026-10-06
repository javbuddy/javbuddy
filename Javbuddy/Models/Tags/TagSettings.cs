namespace Javbuddy.Models;

/// <summary>Single-row settings for tag ignore behavior. Not a Connections-page integration
/// setting, so this deliberately doesn't go through SingleRowSettingsRepository/
/// ConnectionSettingsSaveService — TagService owns it directly.</summary>
public class TagSettings
{
    public int Id { get; set; }

    /// <summary>When true, TagNormalization treats any raw tag value containing non-Latin-script
    /// characters (CJK, Hiragana/Katakana, Hangul, Cyrillic, etc.) as ignored, in addition to the
    /// explicit IgnoredTag list. A simple Unicode-script check, not language detection.</summary>
    public bool AutoIgnoreNonLatinTags { get; set; }
}
