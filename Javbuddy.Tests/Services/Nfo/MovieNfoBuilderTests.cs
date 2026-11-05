using System.Xml.Linq;
using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Nfo;

namespace Javbuddy.Tests.Services.Nfo;

public sealed class MovieNfoBuilderTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "javbuddy-nfobuilder-" + Guid.NewGuid().ToString("N"));

    public MovieNfoBuilderTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public async Task Build_output_round_trips_through_the_nfo_parser()
    {
        var movie = new Movie
        {
            Code = "cospuri-449",
            MetaTitle = "A title",
            MetaDescription = "Plot",
            MetaReleaseDate = new DateTime(2024, 3, 5),
            MetaRuntimeMinutes = 24,
            MetaStudio = "Cospuri",
            MetaGenres = "Uncensored, Cosplay",
            MetaActresses = "Actress One",
        };
        var path = Path.Combine(dir, "cospuri-449.nfo");
        await File.WriteAllTextAsync(path, MovieNfoBuilder.Build(movie));

        var parsed = await LocalNfoParser.ParseAsync(path, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal("A title", parsed.Title);
        Assert.Equal("Plot", parsed.Plot);
        Assert.Equal(new DateTime(2024, 3, 5), parsed.ReleaseDate);
        Assert.Equal(24, parsed.RuntimeMinutes);
        Assert.Equal("Cospuri", parsed.Studio);
        Assert.Equal(["Uncensored", "Cosplay"], parsed.Genres);
        Assert.Equal(["Actress One"], parsed.Actresses);
    }

    [Fact]
    public void Build_falls_back_to_the_code_and_the_probed_duration_and_skips_empty_fields()
    {
        var doc = XDocument.Parse(MovieNfoBuilder.Build(new Movie { Code = "abc-001", MediaDurationSeconds = 1461 }));

        Assert.Equal("abc-001", doc.Root!.Element("title")!.Value);
        Assert.Equal("24", doc.Root.Element("runtime")!.Value);
        Assert.Null(doc.Root.Element("plot"));
        Assert.Null(doc.Root.Element("studio"));
    }

    [Fact]
    public void Build_uses_the_linked_actors_display_names_and_canonical_genres_the_drift_check_expects()
    {
        var movie = new Movie { Code = "abc-001", MetaActresses = "Raw Scraped Name", MetaGenres = "Child" };
        movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Mikami", LastName = "Yua" } });
        movie.MovieTags.Add(new MovieTag { Tag = new Tag { Name = "Child", ParentTag = new Tag { Name = "Parent" } } });

        var root = XDocument.Parse(MovieNfoBuilder.Build(movie)).Root!;

        Assert.Equal([new Actor { FirstName = "Mikami", LastName = "Yua" }.DisplayName], root.Elements("actor").Select(a => a.Element("name")!.Value));
        Assert.Equal(["Parent", "Parent##Child"], root.Elements("genre").Select(g => g.Value));
    }

    [Fact]
    public void Build_writes_first_name_only_actors_and_skips_ones_without_a_first_name()
    {
        var movie = new Movie { Code = "abc-001" };
        movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Yua", LastName = "Mikami" } });
        movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "Madonna", LastName = null } });
        movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = "", LastName = "Smith" } });
        movie.MovieActors.Add(new MovieActor { Actor = new Actor { FirstName = " ", LastName = null } });

        var root = XDocument.Parse(MovieNfoBuilder.Build(movie)).Root!;

        Assert.Equal(["Madonna", "Mikami Yua"], root.Elements("actor").Select(a => a.Element("name")!.Value).Order());
    }

    [Fact]
    public async Task CreateTextAsync_creates_a_new_file_but_never_overwrites_one()
    {
        var path = Path.Combine(dir, "x.nfo");

        Assert.True(await NfoFileWriter.CreateTextAsync(path, "first", CancellationToken.None));
        Assert.False(await NfoFileWriter.CreateTextAsync(path, "second", CancellationToken.None));

        Assert.Equal("first", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
