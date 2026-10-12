namespace Javbuddy.Services.Common;

public static class StringExtensions
{
    public static string? TrimToNull(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Removes every ASCII space and ideographic space (U+3000) so "山田 花子", "山田　花子"
    /// and "山田花子" compare equal — the form used for all name matching.</summary>
    public static string CompactName(this string value) =>
        value.Replace(" ", "").Replace("\u3000", "");
}
