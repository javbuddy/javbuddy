using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Javbuddy.Services.Metrics;

/// <summary>Makes EF Core's query spans readable in a trace list. The instrumentation names every SQLite
/// span after the database ("main"), so a span is renamed to its operation and table ("SELECT Movies").
/// Repeated queries in one trace can't otherwise be told apart (parameter values aren't recorded), so a
/// keyed hash of the values is added: equal hashes mean the same values. The key is random per process
/// and never leaves it, so a hash can't be matched against guessed values.</summary>
public static class DbSpanNaming
{
    public const string ParametersHashTag = "javbuddy.db.parameters_hash";

    private static readonly byte[] HashKey = RandomNumberGenerator.GetBytes(32);

    private static readonly HashSet<string> Operations = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "PRAGMA", "WITH", "CREATE", "DROP", "ALTER", "VACUUM", "BEGIN", "COMMIT", "ROLLBACK", "SAVEPOINT", "RELEASE",
    };

    /// <summary>"{OPERATION} {table}" for <paramref name="sql"/>'s first statement, or just the operation when
    /// no table is found; null when the statement isn't recognised. Never contains a literal value.</summary>
    public static string? SpanName(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return null;

        var tokens = Tokenize(sql);
        if (tokens.Count == 0 || !Operations.Contains(tokens[0].Text)) return null;

        var operation = tokens[0].Text.ToUpperInvariant();
        // A CTE's main statement is the one after it, so it's named by its FROM like a SELECT.
        var keyword = operation switch
        {
            "SELECT" or "WITH" or "DELETE" => "FROM",
            "INSERT" => "INTO",
            "UPDATE" => null, // the table follows directly
            _ => "",
        };
        if (keyword == "") return operation;

        string? table = null;
        if (keyword is null)
        {
            table = tokens.Count > 1 && tokens[1].IsIdentifier ? tokens[1].Text : null;
        }
        else
        {
            // The outermost FROM/INTO names the table; when it's a derived table ("FROM (SELECT ... FROM x)"),
            // the next level in is tried, so a subquery in the select list never wins over the main table.
            for (var depth = 0; depth <= 3 && table is null; depth++)
            {
                for (var i = 1; i < tokens.Count - 1; i++)
                {
                    if (tokens[i].Text == ";" && tokens[i].Depth == 0) break;
                    if (tokens[i].Depth == depth && tokens[i].Text.Equals(keyword, StringComparison.OrdinalIgnoreCase) && tokens[i + 1].IsIdentifier)
                    {
                        table = tokens[i + 1].Text;
                        break;
                    }
                }
            }
        }

        return table is null ? operation : $"{operation} {table}";
    }

    /// <summary>A short keyed hash of <paramref name="command"/>'s parameter names and values, or null when it
    /// has none. Equal within this process for equal values; meaningless across processes.</summary>
    public static string? ParametersHash(IDbCommand command)
    {
        if (command.Parameters.Count == 0) return null;

        var builder = new StringBuilder();
        foreach (IDataParameter parameter in command.Parameters)
        {
            builder.Append(parameter.ParameterName).Append('=');
            builder.Append(parameter.Value switch
            {
                null or DBNull => "\0null",
                byte[] bytes => Convert.ToBase64String(bytes),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                var value => value.ToString(),
            });
            builder.Append('\u001f');
        }

        var hash = HMACSHA256.HashData(HashKey, Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash, 0, 6);
    }

    private readonly record struct Token(string Text, int Depth, bool IsIdentifier);

    // Words, quoted identifiers, parentheses and ';', with the paren depth each sits at. String literals and
    // comments are skipped entirely, so nothing literal can reach a span name.
    private static List<Token> Tokenize(string sql)
    {
        var tokens = new List<Token>();
        var depth = 0;
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 2;
                continue;
            }
            if (c == '\'')
            {
                i++;
                while (i < sql.Length && !(sql[i] == '\'' && (i + 1 >= sql.Length || sql[i + 1] != '\''))) i += sql[i] == '\'' ? 2 : 1;
                i++;
                continue;
            }
            if (c is '"' or '`' or '[')
            {
                var close = c == '[' ? ']' : c;
                var end = sql.IndexOf(close, i + 1);
                if (end < 0) break;
                tokens.Add(new Token(sql[(i + 1)..end], depth, true));
                i = end + 1;
                continue;
            }
            if (c == '(') { tokens.Add(new Token("(", depth, false)); depth++; i++; continue; }
            if (c == ')') { depth = Math.Max(0, depth - 1); tokens.Add(new Token(")", depth, false)); i++; continue; }
            if (c == ';') { tokens.Add(new Token(";", depth, false)); i++; continue; }
            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                var word = sql[start..i];
                // A word straight after FROM/INTO/UPDATE that isn't quoted is still a table name.
                tokens.Add(new Token(word, depth, !Operations.Contains(word) && !IsKeyword(word)));
                continue;
            }
            i++; // operators, parameters' sigils, digits: never part of a name
        }
        return tokens;
    }

    private static bool IsKeyword(string word) => word.ToUpperInvariant() is
        "FROM" or "INTO" or "WHERE" or "AS" or "ON" or "JOIN" or "INNER" or "LEFT" or "OUTER" or "OR" or "AND" or "NOT"
        or "VALUES" or "SET" or "ORDER" or "GROUP" or "BY" or "LIMIT" or "OFFSET" or "RETURNING" or "DISTINCT" or "NULL" or "IS" or "IN" or "EXISTS";
}
