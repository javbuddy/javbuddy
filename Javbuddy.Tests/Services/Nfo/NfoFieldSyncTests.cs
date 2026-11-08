using System.Xml.Linq;
using Javbuddy.Models;
using Javbuddy.Services.Nfo;

namespace Javbuddy.Tests.Services.Nfo;

/// <summary>Creating and filling the scalar elements (other than the title) of an .nfo that lacks them.</summary>
public class NfoFieldSyncTests
{
    private static XElement Load(string xml) => XDocument.Parse(xml, LoadOptions.PreserveWhitespace).Root!;

    private static Movie Full() => new()
    {
        Code = "ABC-123",
        MetaTitle = "T",
        MetaOriginalTitle = "O",
        MetaDescription = "P",
        MetaDirector = "D",
        MetaStudio = "S",
        MetaLabel = "L",
        MetaSeries = "Ser",
        MetaReleaseDate = new DateTime(2024, 6, 15),
        MetaRuntimeMinutes = 118,
    };

    [Fact]
    public void MissingPlot_IsInsertedAfterTheTitle()
    {
        var root = Load("<movie><title>T</title><studio>S</studio></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaDescription = "Overview" };

        Assert.True(NfoFieldSync.ApplyFieldUpdates(movie, root));
        Assert.Equal("<movie><title>T</title><plot>Overview</plot><studio>S</studio></movie>", root.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void EmptyPlot_IsFilledInPlace()
    {
        var root = Load("<movie><title>T</title><plot></plot></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaDescription = "Overview" };

        Assert.True(NfoFieldSync.ApplyFieldUpdates(movie, root));
        Assert.Equal("<movie><title>T</title><plot>Overview</plot></movie>", root.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void MissingFields_AreAllCreated_AndASecondPassChangesNothing()
    {
        var root = Load("<movie><title>T</title></movie>");
        var movie = Full();

        Assert.True(NfoFieldSync.ApplyFieldUpdates(movie, root));
        Assert.Equal("O", root.Element("originaltitle")!.Value);
        Assert.Equal("P", root.Element("plot")!.Value);
        Assert.Equal("D", root.Element("director")!.Value);
        Assert.Equal("S", root.Element("studio")!.Value);
        Assert.Equal("L", root.Element("label")!.Value);
        Assert.Equal("Ser", root.Element("set")!.Element("name")!.Value);
        Assert.Equal("2024-06-15", root.Element("releasedate")!.Value);
        Assert.Equal("118", root.Element("runtime")!.Value);
        Assert.False(NfoFieldSync.ApplyFieldUpdates(movie, root));
    }

    [Fact]
    public void AfterTheSync_TheDriftDetectorReportsNoDrift()
    {
        var root = Load("<movie><id>ABC-123</id></movie>");
        var movie = Full();

        NfoFieldSync.ApplyFieldUpdates(movie, root);

        var outcome = NfoDriftDetector.Evaluate(movie, NfoDriftDetector.ReadNfo(root), baseline: null);
        Assert.Equal(NfoDriftKind.None, outcome.Kind);
    }

    [Theory]
    [InlineData("<movie><title>T</title></movie>")]
    [InlineData("<movie><title>T</title><set/></movie>")]
    [InlineData("<movie><title>T</title><set></set></movie>")]
    [InlineData("<movie><title>T</title><set><name/></set></movie>")]
    [InlineData("<movie><title>T</title><set>\n  <name></name>\n</set></movie>")]
    [InlineData("<movie><title>T</title><set><overview>X</overview></set></movie>")]
    [InlineData("<movie><title>T</title><set>Old Series</set></movie>")]
    [InlineData("<movie><title>T</title><set><name>Old Series</name></set></movie>")]
    public void Series_IsResolvedInEveryShape_SoTheDetectorStopsFlagging(string xml)
    {
        var root = Load(xml);
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaSeries = "Ser" };

        NfoFieldSync.ApplyFieldUpdates(movie, root);

        var outcome = NfoDriftDetector.Evaluate(movie, NfoDriftDetector.ReadNfo(root), baseline: null);
        Assert.Equal(NfoDriftKind.None, outcome.Kind);
        Assert.False(NfoFieldSync.ApplyFieldUpdates(movie, root));
    }

    [Fact]
    public void Studio_IsWrittenToTheMakerDialect_WhenThatIsWhatTheFileUses()
    {
        var root = Load("<movie><title>T</title><maker>Old</maker></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaStudio = "New" };

        Assert.True(NfoFieldSync.ApplyFieldUpdates(movie, root));
        Assert.Equal("New", root.Element("maker")!.Value);
        Assert.Null(root.Element("studio"));
    }

    [Fact]
    public void EmptyStudio_WithAFilledMaker_UpdatesTheMaker()
    {
        var root = Load("<movie><title>T</title><studio></studio><maker>Old</maker></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaStudio = "New" };

        Assert.True(NfoFieldSync.ApplyFieldUpdates(movie, root));
        Assert.Equal("New", root.Element("maker")!.Value);
        Assert.Equal("", root.Element("studio")!.Value);
    }

    [Fact]
    public void ReleaseDate_IsWrittenToThePremieredDialect_WhenThatIsWhatTheFileUses()
    {
        var root = Load("<movie><title>T</title><premiered>2020-01-01</premiered></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaReleaseDate = new DateTime(2024, 6, 15) };

        Assert.True(NfoFieldSync.ApplyFieldUpdates(movie, root));
        Assert.Equal("2024-06-15", root.Element("premiered")!.Value);
        Assert.Null(root.Element("releasedate"));
    }

    [Fact]
    public void ReleaseDate_MatchingTheFilesYearFallback_IsNotWritten()
    {
        var root = Load("<movie><title>T</title><year>2020</year></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaReleaseDate = new DateTime(2020, 1, 1) };

        Assert.False(NfoFieldSync.ApplyFieldUpdates(movie, root));
        Assert.Null(root.Element("releasedate"));
    }

    [Fact]
    public void ReleaseDate_DifferingFromTheFilesYearFallback_IsCreated()
    {
        var root = Load("<movie><title>T</title><year>2020</year></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaReleaseDate = new DateTime(2024, 6, 15) };

        Assert.True(NfoFieldSync.ApplyFieldUpdates(movie, root));
        Assert.Equal("2024-06-15", root.Element("releasedate")!.Value);
    }

    [Fact]
    public void RuntimeWithAUnit_IsOverwritten()
    {
        var root = Load("<movie><title>T</title><runtime>118 min</runtime></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaRuntimeMinutes = 120 };

        Assert.True(NfoFieldSync.ApplyFieldUpdates(movie, root));

        Assert.Equal("120", root.Element("runtime")!.Value);
        Assert.Equal(NfoDriftKind.None, NfoDriftDetector.Evaluate(movie, NfoDriftDetector.ReadNfo(root), baseline: null).Kind);
    }

    [Theory]
    [InlineData("120.0")]
    [InlineData("0120")]
    public void RuntimeThatParsesEqual_IsNotRewritten(string text)
    {
        var root = Load($"<movie><title>T</title><runtime>{text}</runtime></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaRuntimeMinutes = 120 };

        Assert.False(NfoFieldSync.ApplyFieldUpdates(movie, root));
        Assert.Equal(text, root.Element("runtime")!.Value);
    }

    [Fact]
    public void ReleaseDate_WithATimeComponent_IsNotRewritten()
    {
        var root = Load("<movie><title>T</title><releasedate>2024-06-15T10:00:00</releasedate></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaReleaseDate = new DateTime(2024, 6, 15) };

        Assert.False(NfoFieldSync.ApplyFieldUpdates(movie, root));
    }

    [Fact]
    public void UnparsableReleaseDate_WithAValidPremiered_IsOverwrittenAndResolved()
    {
        var root = Load("<movie><title>T</title><releasedate>soon</releasedate><premiered>2020-01-01</premiered></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaReleaseDate = new DateTime(2024, 6, 15) };

        NfoFieldSync.ApplyFieldUpdates(movie, root);

        Assert.Equal(NfoDriftKind.None, NfoDriftDetector.Evaluate(movie, NfoDriftDetector.ReadNfo(root), baseline: null).Kind);
        Assert.False(NfoFieldSync.ApplyFieldUpdates(movie, root));
    }

    [Fact]
    public void MovieWithNoValues_CreatesNothing()
    {
        var root = Load("<movie><id>ABC-123</id></movie>");

        Assert.False(NfoFieldSync.ApplyFieldUpdates(new Movie { Code = "ABC-123" }, root));
        Assert.Equal("<movie><id>ABC-123</id></movie>", root.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void SetWithOtherChildrenAndNoName_ReadsNoSeries_UntilANameIsAdded()
    {
        var root = Load("<movie><title>T</title><set><overview>Ser</overview></set></movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaSeries = "Ser" };

        Assert.Null(NfoDriftDetector.ReadNfo(root).Series);
        Assert.True(NfoFieldSync.ApplyFieldUpdates(movie, root));
        Assert.Equal("Ser", root.Element("set")!.Element("name")!.Value);
    }

    [Fact]
    public async Task MultilineText_WithCrlf_SurvivesAWriteAndReadBackWithoutDrift()
    {
        var dir = Path.Combine(Path.GetTempPath(), "javbuddy-nfo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "ABC-123.nfo");
            await File.WriteAllTextAsync(path, "<movie><title>T</title></movie>");
            var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaDescription = "line one\r\nline two" };

            var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            Assert.True(NfoFieldSync.ApplyFieldUpdates(movie, doc.Root!));
            await NfoFileWriter.WriteXmlAsync(path, doc, CancellationToken.None);

            var reloaded = XDocument.Load(path, LoadOptions.PreserveWhitespace).Root!;
            Assert.Equal(NfoDriftKind.None, NfoDriftDetector.Evaluate(movie, NfoDriftDetector.ReadNfo(reloaded), baseline: null).Kind);
            Assert.False(NfoFieldSync.ApplyFieldUpdates(movie, reloaded));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CreatedElement_CopiesTheAnchorsIndentation()
    {
        var root = Load("<movie>\n  <title>T</title>\n  <studio>S</studio>\n</movie>");
        var movie = new Movie { Code = "ABC-123", MetaTitle = "T", MetaDescription = "Overview" };

        Assert.True(NfoFieldSync.ApplyFieldUpdates(movie, root));
        Assert.Equal("<movie>\n  <title>T</title>\n  <plot>Overview</plot>\n  <studio>S</studio>\n</movie>", root.ToString(SaveOptions.DisableFormatting));
    }
}
