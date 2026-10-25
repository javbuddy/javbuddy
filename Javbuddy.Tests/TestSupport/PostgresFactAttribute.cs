namespace Javbuddy.Tests.TestSupport;

/// <summary>A fact that only makes sense against a real Postgres server: skipped unless <c>JAVBUDDY_TEST_POSTGRES</c> is set
/// (see <see cref="PostgresTestDatabase"/>).</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresTestDatabase.IsEnabled)
        {
            Skip = $"Needs Postgres: set {PostgresTestDatabase.EnvironmentVariable}.";
        }
    }
}
