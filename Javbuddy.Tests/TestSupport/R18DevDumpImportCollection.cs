namespace Javbuddy.Tests.TestSupport;

/// <summary>Test classes that run R18DevDumpImporter.ImportAsync: its process-wide import lock fails a
/// second import outright while one is running, so these classes must not run in parallel.</summary>
[CollectionDefinition(Name)]
public sealed class R18DevDumpImportCollection
{
    public const string Name = "r18.dev dump import";
}
