namespace Javbuddy.Tests.TestSupport;

/// <summary>Test classes that call SqliteConnection.ClearAllPools (directly or via a fixture that does), or
/// that depend on a pooled connection staying pooled. ClearAllPools is process-wide, so it would drop another
/// test's pooled handle mid-run; R18DevDumpImporter.ImportAsync also fails a second concurrent import outright
/// through its process-wide lock. Either way these classes must not run in parallel.</summary>
[CollectionDefinition(Name)]
public sealed class SqlitePoolCollection
{
    public const string Name = "sqlite connection pools";
}
