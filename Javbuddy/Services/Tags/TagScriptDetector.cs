namespace Javbuddy.Services.Tags;

/// <summary>Simple Unicode-block check for the "auto-ignore non-Latin tags" setting — not
/// language detection, just "does this contain a letter outside Latin/common punctuation script."</summary>
public static class TagScriptDetector
{
    public static bool ContainsNonLatinScript(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsLetter(c)) continue;

            var block = char.GetUnicodeCategory(c);
            if (block is not (System.Globalization.UnicodeCategory.UppercaseLetter
                or System.Globalization.UnicodeCategory.LowercaseLetter
                or System.Globalization.UnicodeCategory.TitlecaseLetter
                or System.Globalization.UnicodeCategory.ModifierLetter
                or System.Globalization.UnicodeCategory.OtherLetter))
            {
                continue;
            }

            // Basic Latin, Latin-1 Supplement, Latin Extended-A/B cover ASCII + accented
            // Western-European letters. Anything else lettery (CJK, Hiragana/Katakana, Hangul,
            // Cyrillic, Arabic, etc.) counts as non-Latin.
            var isLatin = (c >= 0x0041 && c <= 0x024F);
            if (!isLatin) return true;
        }

        return false;
    }
}
