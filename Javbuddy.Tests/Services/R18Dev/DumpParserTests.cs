using System.Text;
using Javbuddy.Services.R18Dev;

namespace Javbuddy.Tests.Services.R18Dev;

public class DumpParserTests
{
    private static Stream ToStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task ParseAsync_YieldsOnlyRowsFromWantedTables()
    {
        const string dump = """
            COPY public.videos (content_id, dvd_id) FROM stdin;
            abc123	SIVR-505
            \.
            COPY public.other_table (id) FROM stdin;
            1
            \.
            """;

        var rows = new List<DumpRow>();
        await foreach (var row in DumpParser.ParseAsync(ToStream(dump), new HashSet<string> { "videos" }))
        {
            rows.Add(row);
        }

        var onlyRow = Assert.Single(rows);
        Assert.Equal("videos", onlyRow.Table);
    }

    [Fact]
    public async Task ParseAsync_ParsesColumnsAndValues()
    {
        const string dump = """
            COPY public.videos (content_id, dvd_id, title_en) FROM stdin;
            abc123	SIVR-505	Title One
            \.
            """;

        var rows = new List<DumpRow>();
        await foreach (var row in DumpParser.ParseAsync(ToStream(dump), new HashSet<string> { "videos" }))
        {
            rows.Add(row);
        }

        var result = Assert.Single(rows);
        Assert.Equal(["content_id", "dvd_id", "title_en"], result.Columns);
        Assert.Equal(["abc123", "SIVR-505", "Title One"], result.Values);
    }

    [Fact]
    public async Task ParseAsync_NullMarker_DecodesToNull()
    {
        const string dump = """
            COPY public.videos (content_id, dvd_id) FROM stdin;
            abc123	\N
            \.
            """;

        var rows = new List<DumpRow>();
        await foreach (var row in DumpParser.ParseAsync(ToStream(dump), new HashSet<string> { "videos" }))
        {
            rows.Add(row);
        }

        Assert.Null(rows[0].Values[1]);
    }

    [Fact]
    public async Task ParseAsync_DecodesBackslashEscapeSequences()
    {
        const string dump = "COPY public.videos (title) FROM stdin;\nLine one\\nLine two\\ttabbed\\\\backslash\n\\.\n";

        var rows = new List<DumpRow>();
        await foreach (var row in DumpParser.ParseAsync(ToStream(dump), new HashSet<string> { "videos" }))
        {
            rows.Add(row);
        }

        Assert.Equal("Line one\nLine two\ttabbed\\backslash", rows[0].Values[0]);
    }

    [Fact]
    public async Task ParseAsync_MultipleCopyBlocksForSameTable_AllYielded()
    {
        const string dump = """
            COPY public.videos (id) FROM stdin;
            1
            \.
            COPY public.videos (id) FROM stdin;
            2
            \.
            """;

        var rows = new List<DumpRow>();
        await foreach (var row in DumpParser.ParseAsync(ToStream(dump), new HashSet<string> { "videos" }))
        {
            rows.Add(row);
        }

        Assert.Equal(["1", "2"], rows.Select(r => r.Values[0]));
    }

    [Fact]
    public async Task ParseAsync_NoWantedTables_YieldsNothing()
    {
        const string dump = """
            COPY public.videos (id) FROM stdin;
            1
            \.
            """;

        var rows = new List<DumpRow>();
        await foreach (var row in DumpParser.ParseAsync(ToStream(dump), new HashSet<string> { "nonexistent" }))
        {
            rows.Add(row);
        }

        Assert.Empty(rows);
    }
}
