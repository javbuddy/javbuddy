using Javbuddy.Services.Metrics;
using Microsoft.Data.Sqlite;

namespace Javbuddy.Tests.Services.Metrics;

public class DbSpanNamingTests
{
    [Theory]
    [InlineData("SELECT \"m\".\"Id\", \"m\".\"Code\"\nFROM \"Movies\" AS \"m\"\nWHERE \"m\".\"Status\" = @p", "SELECT Movies")]
    [InlineData("SELECT (\n    SELECT \"s0\".\"Id\"\n    FROM \"ScheduledTaskRuns\" AS \"s0\")\nFROM \"Movies\" AS \"m\"", "SELECT Movies")]
    [InlineData("SELECT \"m0\".\"Id\"\nFROM (\n    SELECT \"m\".\"Id\"\n    FROM \"Movies\" AS \"m\"\n    LIMIT @p\n) AS \"m0\"", "SELECT Movies")]
    [InlineData("SELECT COUNT(*)\nFROM \"CachedImages\" AS \"c\"", "SELECT CachedImages")]
    [InlineData("INSERT INTO \"ScheduledTaskRuns\" (\"EndedAt\") VALUES (@p0)\nRETURNING \"Id\";", "INSERT ScheduledTaskRuns")]
    [InlineData("UPDATE \"Movies\" SET \"Status\" = @p0 WHERE \"Id\" = @p1;\nDELETE FROM \"Tags\" WHERE \"Id\" = @p2;", "UPDATE Movies")]
    [InlineData("DELETE FROM \"ScheduledTaskRuns\" AS \"s\" WHERE \"s\".\"Id\" = @p", "DELETE ScheduledTaskRuns")]
    [InlineData("select id from videos where dvd_id_norm = $norm", "SELECT videos")]
    [InlineData("PRAGMA optimize=0x10002;", "PRAGMA")]
    [InlineData("WITH recent AS (SELECT \"Id\" FROM \"Movies\") SELECT * FROM \"recent\"", "WITH recent")]
    public void SpanName_IsTheOperationAndMainTable(string sql, string expected)
    {
        Assert.Equal(expected, DbSpanNaming.SpanName(sql));
    }

    [Theory]
    [InlineData("SELECT 'FROM Secret' AS x")]
    [InlineData("SELECT 1 -- FROM Secret")]
    [InlineData("SELECT 1 /* FROM Secret */")]
    public void SpanName_NeverTakesATableFromALiteralOrComment(string sql)
    {
        Assert.Equal("SELECT", DbSpanNaming.SpanName(sql));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EXPLAIN QUERY PLAN SELECT 1")]
    public void SpanName_UnrecognisedStatement_IsNull(string? sql)
    {
        Assert.Null(DbSpanNaming.SpanName(sql));
    }

    private static SqliteCommand Command(params (string Name, object? Value)[] parameters)
    {
        var command = new SqliteCommand("SELECT 1");
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    [Fact]
    public void ParametersHash_SameValues_SameHash_DifferentValues_DifferentHash()
    {
        var a = DbSpanNaming.ParametersHash(Command(("@ids1", 7), ("@ids2", 9)));
        var b = DbSpanNaming.ParametersHash(Command(("@ids1", 7), ("@ids2", 9)));
        var c = DbSpanNaming.ParametersHash(Command(("@ids1", 7), ("@ids2", 10)));

        Assert.NotNull(a);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(12, a.Length);
    }

    [Fact]
    public void ParametersHash_DistinguishesNullAndNamesAndNeverContainsTheValue()
    {
        var withNull = DbSpanNaming.ParametersHash(Command(("@code", null)));
        var withText = DbSpanNaming.ParametersHash(Command(("@code", "MIDV-461")));
        var otherName = DbSpanNaming.ParametersHash(Command(("@other", "MIDV-461")));

        Assert.NotEqual(withNull, withText);
        Assert.NotEqual(withText, otherName);
        Assert.DoesNotContain("MIDV", withText!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParametersHash_NoParameters_IsNull()
    {
        Assert.Null(DbSpanNaming.ParametersHash(Command()));
    }
}
