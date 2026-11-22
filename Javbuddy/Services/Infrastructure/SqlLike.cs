namespace Javbuddy.Services.Infrastructure;

/// <summary>
/// Builds <c>LIKE</c> patterns for <c>EF.Functions.Like</c>. SQLite's LIKE ignores ASCII case and compares every
/// other character exactly, which is what the NOCASE name columns do too. C#'s <c>ToUpper()</c> folds non-ASCII
/// letters that SQLite's <c>UPPER()</c> leaves alone, so the two sides of such a comparison never agree.
/// </summary>
public static class SqlLike
{
    public const string EscapeChar = "\\";

    /// <summary>A "contains" pattern for <paramref name="term"/>, with <c>%</c>, <c>_</c> and the escape character taken literally.</summary>
    public static string Contains(string term) => "%" + Escape(term) + "%";

    /// <summary>A "starts with" pattern for <paramref name="term"/>, with <c>%</c>, <c>_</c> and the escape character taken literally.</summary>
    public static string StartsWith(string term) => Escape(term) + "%";

    private static string Escape(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
