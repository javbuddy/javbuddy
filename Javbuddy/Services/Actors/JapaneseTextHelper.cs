namespace Javbuddy.Services.Actors;

/// <summary>Helper functions for identifying and classifying Japanese text in actor metadata.</summary>
public static class JapaneseTextHelper
{
    /// <summary>Returns true if the string contains any Japanese characters (Hiragana, Katakana, or Kanji).</summary>
    public static bool HasJapaneseCharacters(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var c in text)
        {
            if (IsHiragana(c) || IsKatakana(c) || IsKanji(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns true if the string contains any CJK ideographs (Kanji).</summary>
    public static bool HasKanji(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var c in text)
        {
            if (IsKanji(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns true if the string contains any Hiragana or Katakana characters.</summary>
    public static bool HasKana(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var c in text)
        {
            if (IsHiragana(c) || IsKatakana(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns true if the string contains Kana and has no Kanji characters.</summary>
    public static bool IsPureKana(string? text) => HasKana(text) && !HasKanji(text);

    /// <summary>Returns true if the character is in the Hiragana Unicode block.</summary>
    public static bool IsHiragana(char c) => c is >= '\u3040' and <= '\u309F';

    /// <summary>Returns true if the character is in the Katakana Unicode block or phonetic extensions.</summary>
    public static bool IsKatakana(char c) => c is (>= '\u30A0' and <= '\u30FF') or (>= '\u31F0' and <= '\u31FF');

    /// <summary>Returns true if the character is in the CJK Unified Ideographs or extension blocks.</summary>
    public static bool IsKanji(char c) =>
        c is (>= '\u4E00' and <= '\u9FFF')
            or (>= '\u3400' and <= '\u4DBF')
            or (>= '\uF900' and <= '\uFAFF');
}
