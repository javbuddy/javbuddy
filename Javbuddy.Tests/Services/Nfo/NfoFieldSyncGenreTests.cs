using System.Xml.Linq;
using Javbuddy.Models;
using Javbuddy.Services.Nfo;

namespace Javbuddy.Tests.Services.Nfo;

/// <summary>Creating the &lt;genre&gt; elements of an .nfo that has none — the one element NfoFieldSync invents.</summary>
public class NfoFieldSyncGenreTests
{
    private static Movie MovieWithTags(params string[] tags)
    {
        var movie = new Movie { Code = "ABC-123" };
        foreach (var name in tags) movie.MovieTags.Add(new MovieTag { Movie = movie, Tag = new Tag { Name = name } });
        return movie;
    }

    private static XElement Load(string xml) => XDocument.Parse(xml, LoadOptions.PreserveWhitespace).Root!;

    [Fact]
    public void GenresAreInsertedBeforeTheFirstActor_WithTheFilesIndentation()
    {
        var root = Load("<movie>\n  <title>T</title>\n  <actor><name>A</name></actor>\n</movie>");

        var changed = NfoFieldSync.ApplyFieldUpdates(MovieWithTags("Drama", "Cosplay"), root);

        Assert.True(changed);
        Assert.Equal("<movie>\n  <title>T</title>\n  <genre>Cosplay</genre>\n  <genre>Drama</genre>\n  <actor><name>A</name></actor>\n</movie>", root.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void WithoutAnActor_GenresGoAfterTheLastElement_BeforeTheClosingWhitespace()
    {
        var root = Load("<movie>\n  <title>T</title>\n</movie>");

        NfoFieldSync.ApplyFieldUpdates(MovieWithTags("Drama", "Cosplay"), root);

        Assert.Equal("<movie>\n  <title>T</title>\n  <genre>Cosplay</genre>\n  <genre>Drama</genre>\n</movie>", root.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void AnEmptyRootJustGetsTheGenres()
    {
        var root = Load("<movie />");

        Assert.True(NfoFieldSync.ApplyFieldUpdates(MovieWithTags("Drama"), root));
        Assert.Equal(["Drama"], root.Elements("genre").Select(e => e.Value));
    }

    [Fact]
    public void AChildTagIsWrittenWithItsParent_AndReadsBackAsTheSameGenreSet()
    {
        var movie = new Movie { Code = "ABC-123" };
        movie.MovieTags.Add(new MovieTag { Movie = movie, Tag = new Tag { Name = "Rough", ParentTag = new Tag { Name = "Play" } } });
        var root = Load("<movie>\n  <title>T</title>\n</movie>");

        NfoFieldSync.ApplyFieldUpdates(movie, root);

        Assert.Equal(["Play", "Play##Rough"], root.Elements("genre").Select(e => e.Value));
        var outcome = NfoDriftDetector.Evaluate(movie, NfoDriftDetector.ReadNfo(root), baseline: null, strict: false);
        Assert.DoesNotContain("Genres:", outcome.Details ?? "");
    }

    [Fact]
    public void AMovieWithoutTagsLeavesTheNfoAlone()
    {
        var root = Load("<movie>\n  <title>T</title>\n</movie>");

        Assert.False(NfoFieldSync.ApplyFieldUpdates(MovieWithTags(), root));
        Assert.Empty(root.Elements("genre"));
    }

    [Fact]
    public void AfterTheGenresAreWritten_TheDriftCheckAgrees_AndASecondRunChangesNothing()
    {
        var movie = MovieWithTags("Drama", "Cosplay");
        var root = Load("<movie>\n  <title>T</title>\n  <actor><name>A</name></actor>\n</movie>");

        NfoFieldSync.ApplyFieldUpdates(movie, root);

        Assert.Equal(NfoDriftDetector.CanonicalGenres(movie), NfoDriftDetector.ReadNfo(root).Genres.Order(StringComparer.OrdinalIgnoreCase));
        Assert.False(NfoFieldSync.ApplyFieldUpdates(movie, root));
    }
}
