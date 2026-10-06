using System.Runtime.CompilerServices;
using System.Text;

namespace Javbuddy.Services.R18Dev;

/// <summary>A row from one COPY block of a PostgreSQL text-format pg_dump: its table name, the
/// column names from the COPY header, and tab-delimited values (NULL markers decoded to null).</summary>
public sealed record DumpRow(string Table, IReadOnlyList<string> Columns, IReadOnlyList<string?> Values);

/// <summary>Streams a PostgreSQL pg_dump (plain SQL text, as produced by pg_dump and used by
/// r18.dev's public database dump) and yields rows from COPY blocks for a caller-supplied set of
/// tables, skipping every other table's rows without decoding them. A dump is a sequence of
/// "COPY public.&lt;table&gt; (&lt;cols&gt;) FROM stdin;" headers, each followed by tab-separated data
/// lines and terminated by a lone "\." line; NULL is the literal two-character "\N" and other
/// special characters are backslash-escaped (\n \t \r \b \f \v \\). Mirrors javinizer-go's own
/// r18.dev dump importer (internal/r18devdump/parse.go), reimplemented for .NET.</summary>
public static class DumpParser
{
    public static async IAsyncEnumerable<DumpRow> ParseAsync(
        Stream stream,
        IReadOnlySet<string> tablesOfInterest,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 20, leaveOpen: true);

        string? table = null;
        IReadOnlyList<string>? columns = null;
        var inCopy = false;
        var wanted = false;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();

            if (!inCopy)
            {
                if (TryParseCopyHeader(line, out var headerTable, out var headerColumns))
                {
                    table = headerTable;
                    columns = headerColumns;
                    inCopy = true;
                    wanted = tablesOfInterest.Contains(table);
                }
                continue;
            }

            if (line == "\\.")
            {
                inCopy = false;
                table = null;
                columns = null;
                wanted = false;
                continue;
            }

            if (!wanted) continue;

            var rawValues = line.Split('\t');
            var values = new string?[rawValues.Length];
            for (var i = 0; i < rawValues.Length; i++)
            {
                values[i] = rawValues[i] == "\\N" ? null : DecodeField(rawValues[i]);
            }

            yield return new DumpRow(table!, columns!, values);
        }
    }

    /// <summary>Parses a "COPY public.&lt;table&gt; (col1, col2, ...) FROM stdin;" line. r18.dev's
    /// dump always uses unquoted lowercase identifiers, which is the only form handled here.</summary>
    private static bool TryParseCopyHeader(string line, out string table, out IReadOnlyList<string> columns)
    {
        table = "";
        columns = Array.Empty<string>();

        const string prefix = "COPY public.";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;

        var rest = line[prefix.Length..];
        var nameEnd = rest.IndexOfAny([' ', '(']);
        if (nameEnd < 0) return false;
        table = rest[..nameEnd];

        var openParen = line.IndexOf('(');
        var closeParen = line.LastIndexOf(')');
        if (openParen < 0 || closeParen < 0 || closeParen < openParen)
        {
            return true; // COPY with no explicit column list — table matched, columns stay empty.
        }

        columns = line[(openParen + 1)..closeParen]
            .Split(',')
            .Select(c => c.Trim().Trim('"'))
            .ToArray();
        return true;
    }

    private static string DecodeField(string raw)
    {
        if (!raw.Contains('\\')) return raw;

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\\')
            {
                sb.Append(raw[i]);
                continue;
            }
            if (i + 1 >= raw.Length)
            {
                sb.Append('\\');
                continue;
            }
            var next = raw[i + 1];
            switch (next)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'v': sb.Append('\v'); break;
                case '\\': sb.Append('\\'); break;
                default:
                    sb.Append('\\');
                    sb.Append(next);
                    break;
            }
            i++;
        }
        return sb.ToString();
    }
}
