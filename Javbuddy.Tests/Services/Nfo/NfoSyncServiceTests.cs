using System.Xml.Linq;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Services.Nfo;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Services.Nfo;

public class NfoSyncServiceTests
{
    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "javbuddy-tests-" + Guid.NewGuid().ToString("N"));

        public TempRoot() => Directory.CreateDirectory(Path);

        public string AddMovieFolder(string folderName)
        {
            var folder = System.IO.Path.Combine(Path, folderName);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static LocalLibraryClient CreateClientFor(TestDbContextFactory factory, TempRoot root)
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["LocalLibrary:RootPaths:0"] = root.Path
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var mediaInfo = Substitute.For<IMediaInfoProber>();
        return new LocalLibraryClient(
            factory,
            config,
            new MemoryCache(new MemoryCacheOptions()),
            mediaInfo,
            NullLogger<LocalLibraryClient>.Instance);
    }

    /// <summary>Resolves the given genre names onto the real canonical Tags/MovieTags relation for
    /// an already-saved movie — NfoSyncService's genre conflict-detection/write now reads that
    /// relation (via Movie.MovieTags), not MetaGenres's comma-joined cache string.</summary>
    private static void AddGenreTags(AppDbContext db, Movie movie, params string[] genres)
    {
        foreach (var name in genres)
        {
            var tag = db.Tags.FirstOrDefault(t => t.Name == name);
            if (tag is null)
            {
                tag = new Tag { Name = name };
                db.Tags.Add(tag);
                db.SaveChanges();
            }
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = tag.Id });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task PreviewSyncActorAsync_ReturnsCorrectStatuses()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        // Movie 1: Needs update (contains alias "Yua Mikami")
        var folder1 = root.AddMovieFolder("IPX-001");
        var nfo1 = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>IPX-001 Title</title>
              <actor>
                <name>Yua Mikami</name>
                <role>Actress</role>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder1, "IPX-001.nfo"), nfo1);

        // Movie 2: Already up to date (contains canonical "Mikami Yua")
        var folder2 = root.AddMovieFolder("IPX-002");
        var nfo2 = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>IPX-002 Title</title>
              <actor>
                <name>Mikami Yua</name>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder2, "IPX-002.nfo"), nfo2);

        // Movie 3: No .nfo file
        root.AddMovieFolder("IPX-003");

        int actorId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami"
            };
            actor.Aliases.Add(new ActorAlias { Name = "Yua Mikami" });
            db.Actors.Add(actor);
            db.SaveChanges();
            actorId = actor.Id;

            var m1 = new Movie { Code = "IPX-001", Title = "Movie 1", MetaActresses = "Yua Mikami" };
            var m2 = new Movie { Code = "IPX-002", Title = "Movie 2", MetaActresses = "Mikami Yua" };
            var m3 = new Movie { Code = "IPX-003", Title = "Movie 3", MetaActresses = "Mikami Yua" };
            db.Movies.AddRange(m1, m2, m3);
            db.SaveChanges();

            db.MovieActors.AddRange(
                new MovieActor { MovieId = m1.Id, ActorId = actorId },
                new MovieActor { MovieId = m2.Id, ActorId = actorId },
                new MovieActor { MovieId = m3.Id, ActorId = actorId });
            db.SaveChanges();
        }

        var preview = await service.PreviewSyncActorAsync(actorId);

        Assert.Equal("Mikami Yua", preview.CanonicalName);
        Assert.Equal(3, preview.TotalMovies);
        Assert.Equal(1, preview.WillUpdateCount);
        Assert.Equal(1, preview.UpToDateCount);
        Assert.Equal(1, preview.NoNfoCount);

        var p1 = preview.Movies.Single(m => m.MovieCode == "IPX-001");
        Assert.Equal(ActorNfoMovieStatus.WillUpdate, p1.Status);
        Assert.Equal(["Yua Mikami"], p1.CurrentNames);

        var p2 = preview.Movies.Single(m => m.MovieCode == "IPX-002");
        Assert.Equal(ActorNfoMovieStatus.UpToDate, p2.Status);

        var p3 = preview.Movies.Single(m => m.MovieCode == "IPX-003");
        Assert.Equal(ActorNfoMovieStatus.NoNfoFound, p3.Status);
    }

    [Fact]
    public async Task PreviewSyncActorAsync_ReadOnlyFile_ReturnsReadOnlyStatus()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("IPX-004");
        var nfoPath = Path.Combine(folder, "IPX-004.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie><actor><name>Mikami Yua</name></actor></movie>");
        File.SetAttributes(nfoPath, FileAttributes.ReadOnly);

        int actorId;
        try
        {
            using (var db = factory.CreateDbContext())
            {
                var actor = new Actor
                {
                    FirstName = "Yua",
                    LastName = "Mikami"
                };
                actor.Aliases.Add(new ActorAlias { Name = "Mikami Yua" });
                db.Actors.Add(actor);
                db.SaveChanges();
                actorId = actor.Id;

                var m = new Movie { Code = "IPX-004", MetaActresses = "Mikami Yua" };
                db.Movies.Add(m);
                db.SaveChanges();

                db.MovieActors.Add(new MovieActor { MovieId = m.Id, ActorId = actorId });
                db.SaveChanges();
            }

            var preview = await service.PreviewSyncActorAsync(actorId);
            var item = Assert.Single(preview.Movies);
            Assert.Equal(ActorNfoMovieStatus.ReadOnly, item.Status);
            Assert.Equal(1, preview.ProblemCount);
        }
        finally
        {
            File.SetAttributes(nfoPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task SyncActorAsync_PreservesWhitespaceAndDeclaration_UpdatesActorName()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("FGAN-188");
        var nfoPath = Path.Combine(folder, "FGAN-188.nfo");
        var originalXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>FGAN-188 Sample Movie</title>
              <director>Sample Director</director>
              <actor>
                <name>Momose Askura</name>
                <thumb>https://example.com/photo.jpg</thumb>
              </actor>
              <actor>
                <name>Emiri Takayama</name>
                <order>1</order>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, originalXml);

        int actorId;
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor
            {
                FirstName = "Momose",
                LastName = "Asakura"
            };
            actor.Aliases.Add(new ActorAlias { Name = "Momose Askura" });
            db.Actors.Add(actor);
            db.SaveChanges();
            actorId = actor.Id;

            var movie = new Movie
            {
                Code = "FGAN-188",
                MetaTitle = "FGAN-188 Sample Movie",
                MetaActresses = "Momose Askura, Emiri Takayama"
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;

            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actorId });
            db.SaveChanges();
        }

        var result = await service.SyncActorAsync(actorId);

        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.FailedCount);

        var updatedXml = await File.ReadAllTextAsync(nfoPath);

        // Verify declaration is intact
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", updatedXml.TrimStart());

        // Verify actor name was changed
        Assert.Contains("<name>Asakura Momose</name>", updatedXml);
        Assert.DoesNotContain("<name>Momose Askura</name>", updatedXml);

        // Verify other actor and fields are intact with whitespace
        Assert.Contains("<name>Emiri Takayama</name>", updatedXml);
        Assert.Contains("<order>1</order>", updatedXml);
        Assert.Contains("<thumb>https://example.com/photo.jpg</thumb>", updatedXml);
        Assert.Contains("  <director>Sample Director</director>", updatedXml);

        // The pre-sync file is kept as a history generation, not a .bak in the folder
        Assert.Equal([(NfoWriteTrigger.ActorSync, originalXml)], NfoHistory(factory, "FGAN-188"));
        Assert.False(File.Exists(nfoPath + ".bak"));

        // Verify DB MetaActresses was updated
        using (var verifyDb = factory.CreateDbContext())
        {
            var dbMovie = verifyDb.Movies.Single(m => m.Id == movieId);
            Assert.Equal("Asakura Momose, Emiri Takayama", dbMovie.MetaActresses);
            Assert.NotNull(dbMovie.MediaNfoLastWriteUtc);
            Assert.Equal(File.GetLastWriteTimeUtc(nfoPath), dbMovie.MediaNfoLastWriteUtc.Value);
        }

        // Verify RefreshLocalMetadataIfNfoChangedAsync sees no external conflict
        var refreshResult = await client.RefreshLocalMetadataIfNfoChangedAsync(movieId);
        Assert.True(refreshResult.Success);
    }

    [Fact]
    public async Task SyncActorAsync_ConsolidatesDuplicateActorElements_MergingThumbAndRole()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("SIVR-505");
        var nfoPath = Path.Combine(folder, "SIVR-505.nfo");
        var originalXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>SIVR-505</title>
              <actor>
                <name>Mikami Yua</name>
                <role>Lead Actress</role>
              </actor>
              <actor>
                <name>Yua Mikami</name>
                <thumb>https://pics.example.com/yua.jpg</thumb>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, originalXml);

        int actorId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami"
            };
            actor.Aliases.Add(new ActorAlias { Name = "Mikami Yua" });
            db.Actors.Add(actor);
            db.SaveChanges();
            actorId = actor.Id;

            var movie = new Movie
            {
                Code = "SIVR-505",
                MetaActresses = "Mikami Yua, Yua Mikami"
            };
            db.Movies.Add(movie);
            db.SaveChanges();

            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actorId });
            db.SaveChanges();
        }

        var result = await service.SyncActorAsync(actorId);

        Assert.Equal(1, result.UpdatedCount);

        var updatedDoc = XDocument.Parse(await File.ReadAllTextAsync(nfoPath));
        var actors = updatedDoc.Root!.Elements("actor").ToList();

        // Should now have exactly ONE actor element for Mikami Yua
        var singleActor = Assert.Single(actors);
        Assert.Equal("Mikami Yua", singleActor.Element("name")?.Value);
        Assert.Equal("Lead Actress", singleActor.Element("role")?.Value);
        Assert.Equal("https://pics.example.com/yua.jpg", singleActor.Element("thumb")?.Value);
    }

    [Fact]
    public async Task SyncAllActorsAsync_ScansAndAlignsLibraryWide()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder1 = root.AddMovieFolder("MOV-001");
        await File.WriteAllTextAsync(Path.Combine(folder1, "MOV-001.nfo"), "<movie><actor><name>Hatano, Yui</name></actor></movie>");

        var folder2 = root.AddMovieFolder("MOV-002");
        await File.WriteAllTextAsync(Path.Combine(folder2, "MOV-002.nfo"), "<movie><actor><name>Yua Mikami</name></actor></movie>");

        using (var db = factory.CreateDbContext())
        {
            var a1 = new Actor { FirstName = "Yui", LastName = "Hatano" };
            a1.Aliases.Add(new ActorAlias { Name = "Hatano, Yui" });
            var a2 = new Actor { FirstName = "Yua", LastName = "Mikami" };
            a2.Aliases.Add(new ActorAlias { Name = "Yua Mikami" });
            db.Actors.AddRange(a1, a2);
            db.Movies.AddRange(
                new Movie { Code = "MOV-001", MetaActresses = "Hatano, Yui" },
                new Movie { Code = "MOV-002", MetaActresses = "Yua Mikami" });
            db.SaveChanges();
        }

        var batchResult = await service.SyncAllActorsAsync();

        Assert.Equal(2, batchResult.TotalMoviesChecked);
        Assert.Equal(2, batchResult.TotalNfosUpdated);
        Assert.Equal(2, batchResult.TotalActorsUpdated);
        Assert.Equal(0, batchResult.FailedCount);

        var nfo1 = await File.ReadAllTextAsync(Path.Combine(folder1, "MOV-001.nfo"));
        var nfo2 = await File.ReadAllTextAsync(Path.Combine(folder2, "MOV-002.nfo"));

        Assert.Contains("<name>Hatano Yui</name>", nfo1);
        Assert.Contains("<name>Mikami Yua</name>", nfo2);
        Assert.Equal([(NfoWriteTrigger.ActorSync, "<movie><actor><name>Hatano, Yui</name></actor></movie>")], NfoHistory(factory, "MOV-001"));
        Assert.Equal([(NfoWriteTrigger.ActorSync, "<movie><actor><name>Yua Mikami</name></actor></movie>")], NfoHistory(factory, "MOV-002"));
    }

    [Fact]
    public async Task CheckMovieNfoConflictAsync_DetectsNameDivergence()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("SNIS-123");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>SNIS-123</title>
              <actor>
                <name>Yua Mikami</name>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder, "SNIS-123.nfo"), nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            actor.Aliases.Add(new ActorAlias { Name = "Yua Mikami" });
            db.Actors.Add(actor);
            var movie = new Movie { Code = "SNIS-123", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = actor.Id });
            db.SaveChanges();
        }

        var result = await service.CheckMovieNfoConflictAsync(movieId);

        Assert.True(result.HasConflict);
        Assert.Contains("Mikami Yua", result.ConflictDetails);
        Assert.Contains("Yua Mikami", result.ConflictDetails);

        using (var db = factory.CreateDbContext())
        {
            var savedMovie = await db.Movies.FindAsync(movieId);
            Assert.NotNull(savedMovie);
            Assert.NotEqual(NfoDriftKind.None, savedMovie.NfoDriftKind);
            Assert.NotNull(savedMovie.NfoConflictDetails);
        }
    }

    [Fact]
    public async Task CheckMovieNfoConflictAsync_ClearsConflictWhenUpToDate()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("SNIS-124");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>SNIS-124</title>
              <actor>
                <name>Mikami Yua</name>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder, "SNIS-124.nfo"), nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            var movie = new Movie { Code = "SNIS-124", Status = MovieStatus.Got, NfoDriftKind = NfoDriftKind.ExternalEdit, NfoConflictDetails = "Old conflict" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = actor.Id });
            db.SaveChanges();
        }

        var result = await service.CheckMovieNfoConflictAsync(movieId);

        Assert.False(result.HasConflict);
        Assert.Null(result.ConflictDetails);

        using (var db = factory.CreateDbContext())
        {
            var savedMovie = await db.Movies.FindAsync(movieId);
            Assert.NotNull(savedMovie);
            Assert.Equal(NfoDriftKind.None, savedMovie.NfoDriftKind);
            Assert.Null(savedMovie.NfoConflictDetails);
        }
    }

    [Fact]
    public async Task SyncMovieNfoAsync_UpdatesNfoAndClearsConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("SNIS-125");
        var nfoPath = Path.Combine(folder, "SNIS-125.nfo");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>SNIS-125</title>
              <actor>
                <name>Yua Mikami</name>
                <role>Actress</role>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            actor.Aliases.Add(new ActorAlias { Name = "Yua Mikami" });
            db.Actors.Add(actor);
            var movie = new Movie { Code = "SNIS-125", Status = MovieStatus.Got, NfoDriftKind = NfoDriftKind.ExternalEdit, NfoConflictDetails = "Conflict" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = actor.Id });
            db.SaveChanges();
        }

        var syncResult = await service.SyncMovieNfoAsync(movieId);

        Assert.Equal(1, syncResult.UpdatedCount);

        var updatedXml = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<name>Mikami Yua</name>", updatedXml);
        Assert.Equal([(NfoWriteTrigger.ActorSync, nfo)], NfoHistory(factory, "SNIS-125"));

        using (var db = factory.CreateDbContext())
        {
            var savedMovie = await db.Movies.FindAsync(movieId);
            Assert.NotNull(savedMovie);
            Assert.Equal(NfoDriftKind.None, savedMovie.NfoDriftKind);
            Assert.Null(savedMovie.NfoConflictDetails);
        }
    }

    [Fact]
    public async Task GenerateProposedNfoAsync_ReturnsUpdatedXml_WithoutModifyingDiskOrClearingConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("SNIS-126");
        var nfoPath = Path.Combine(folder, "SNIS-126.nfo");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>SNIS-126</title>
              <actor>
                <name>Yua Mikami</name>
                <role>Actress</role>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            actor.Aliases.Add(new ActorAlias { Name = "Yua Mikami" });
            db.Actors.Add(actor);
            var movie = new Movie { Code = "SNIS-126", Status = MovieStatus.Got, NfoDriftKind = NfoDriftKind.ExternalEdit, NfoConflictDetails = "Conflict" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = actor.Id });
            db.SaveChanges();
        }

        var proposed = await service.GenerateProposedNfoAsync(movieId);

        Assert.NotNull(proposed);
        Assert.Contains("<name>Mikami Yua</name>", proposed);

        // Verify on-disk file was NOT modified
        var diskXml = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<name>Yua Mikami</name>", diskXml);
        Assert.DoesNotContain("<name>Mikami Yua</name>", diskXml);

        // Verify database conflict state was NOT cleared
        using (var db = factory.CreateDbContext())
        {
            var savedMovie = await db.Movies.FindAsync(movieId);
            Assert.NotNull(savedMovie);
            Assert.NotEqual(NfoDriftKind.None, savedMovie.NfoDriftKind);
            Assert.Equal("Conflict", savedMovie.NfoConflictDetails);
        }
    }

    [Fact]
    public async Task ResolveMovieNfoConflictAsync_WritesToDisk_AndClearsConflictInDb()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("SNIS-127");
        var nfoPath = Path.Combine(folder, "SNIS-127.nfo");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>SNIS-127</title>
              <actor>
                <name>Yua Mikami</name>
                <role>Actress</role>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            actor.Aliases.Add(new ActorAlias { Name = "Yua Mikami" });
            db.Actors.Add(actor);
            var movie = new Movie { Code = "SNIS-127", Status = MovieStatus.Got, NfoDriftKind = NfoDriftKind.ExternalEdit, NfoConflictDetails = "Conflict" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = actor.Id });
            db.SaveChanges();
        }

        var proposed = await service.GenerateProposedNfoAsync(movieId);
        Assert.NotNull(proposed);

        var newerNfo = nfo.Replace("Yua Mikami", "Scraper Changed", StringComparison.Ordinal);
        await File.WriteAllTextAsync(nfoPath, newerNfo);

        var staleResult = await service.ResolveMovieNfoConflictAsync(movieId, nfo, proposed);
        Assert.False(staleResult.Success);
        Assert.True(staleResult.FileChanged);
        Assert.Equal(newerNfo, await File.ReadAllTextAsync(nfoPath));
        Assert.Empty(NfoHistory(factory, "SNIS-127"));

        proposed = await service.GenerateProposedNfoAsync(movieId);
        Assert.NotNull(proposed);
        var result = await service.ResolveMovieNfoConflictAsync(movieId, newerNfo, proposed);
        Assert.True(result.Success);

        // Verify on-disk file WAS updated, with the version it replaced kept in the history
        var diskXml = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<name>Mikami Yua</name>", diskXml);
        Assert.Equal([(NfoWriteTrigger.ConflictResolution, newerNfo)], NfoHistory(factory, "SNIS-127"));

        // Verify database conflict state WAS cleared
        using (var db = factory.CreateDbContext())
        {
            var savedMovie = await db.Movies.FindAsync(movieId);
            Assert.NotNull(savedMovie);
            Assert.Equal(NfoDriftKind.None, savedMovie.NfoDriftKind);
            Assert.Null(savedMovie.NfoConflictDetails);
            Assert.NotNull(savedMovie.MediaNfoLastWriteUtc);
        }
    }

    [Fact]
    public async Task ResolveMovieNfoConflictAsync_WithInvalidXml_ReturnsFalseAndDoesNotModifyDiskOrDb()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("SNIS-128");
        var nfoPath = Path.Combine(folder, "SNIS-128.nfo");
        var nfo = "<movie><name>Yua Mikami</name>";
        await File.WriteAllTextAsync(nfoPath, nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "SNIS-128", Status = MovieStatus.Got, NfoDriftKind = NfoDriftKind.ExternalEdit, NfoConflictDetails = "Conflict" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var result = await service.ResolveMovieNfoConflictAsync(movieId, nfo, "<<<invalid xml>>>");
        Assert.False(result.Success);

        var proposed = await service.GenerateProposedNfoAsync(movieId);
        Assert.Null(proposed);

        // Disk unchanged
        var diskXml = await File.ReadAllTextAsync(nfoPath);
        Assert.Equal(nfo, diskXml);

        // DB unchanged
        using (var db = factory.CreateDbContext())
        {
            var savedMovie = await db.Movies.FindAsync(movieId);
            Assert.NotNull(savedMovie);
            Assert.NotEqual(NfoDriftKind.None, savedMovie.NfoDriftKind);
        }
    }

    [Fact]
    public async Task DetectAllMovieConflictsAsync_FlagsConflictingGotMovies()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder1 = root.AddMovieFolder("CONF-001");
        await File.WriteAllTextAsync(Path.Combine(folder1, "CONF-001.nfo"), "<movie><actor><name>Old Name</name></actor></movie>");

        var folder2 = root.AddMovieFolder("CONF-002");
        await File.WriteAllTextAsync(Path.Combine(folder2, "CONF-002.nfo"), "<movie><actor><name>Canonical Name</name></actor></movie>");

        using (var db = factory.CreateDbContext())
        {
            var a1 = new Actor { FirstName = "Name", LastName = "Canonical" };
            a1.Aliases.Add(new ActorAlias { Name = "Old Name" });
            db.Actors.Add(a1);

            var m1 = new Movie { Code = "CONF-001", Status = MovieStatus.Got };
            var m2 = new Movie { Code = "CONF-002", Status = MovieStatus.Got };
            db.Movies.AddRange(m1, m2);
            db.SaveChanges();

            db.MovieActors.AddRange(
                new MovieActor { MovieId = m1.Id, ActorId = a1.Id },
                new MovieActor { MovieId = m2.Id, ActorId = a1.Id });
            db.SaveChanges();
        }

        var batchResult = await service.DetectAllMovieConflictsAsync();

        Assert.Equal(2, batchResult.TotalMoviesChecked);
        Assert.Equal(1, batchResult.ConflictsFoundCount);

        using (var db = factory.CreateDbContext())
        {
            var m1 = db.Movies.First(m => m.Code == "CONF-001");
            var m2 = db.Movies.First(m => m.Code == "CONF-002");
            Assert.NotEqual(NfoDriftKind.None, m1.NfoDriftKind);
            Assert.Equal(NfoDriftKind.None, m2.NfoDriftKind);
        }
    }

    [Fact]
    public async Task Rescan_WhenActorRenamedInJavbuddy_PreservesAuthoritativeJavbuddyCastAndFlagsConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);
        var actorService = new ActorService(factory, nfoSyncService: service);

        var folder = root.AddMovieFolder("REN-001");
        var nfoPath = Path.Combine(folder, "REN-001.nfo");
        var originalXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>Authoritative Movie</title>
              <actor>
                <name>Hatano Yui</name>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, originalXml);
        var initialWriteUtc = File.GetLastWriteTimeUtc(nfoPath);

        int actorId;
        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.Add(actor);
            db.SaveChanges();
            actorId = actor.Id;

            var movie = new Movie
            {
                Code = "REN-001",
                MetaTitle = "Authoritative Movie",
                MetaActresses = "Hatano Yui",
                MetaSourceName = "Local",
                Status = MovieStatus.Got,
                MediaNfoLastWriteUtc = initialWriteUtc
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;

            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = actorId });
            db.SaveChanges();
        }

        // 1. Rename actor in Javbuddy
        var updateResult = await actorService.UpdateAsync(new ActorUpdateModel
        {
            Id = actorId,
            FirstName = "Yui",
            LastName = "UpdatedHatano"
        });
        Assert.True(updateResult.Success);

        // Verify Javbuddy updated the movie cast and preserved the alias
        using (var db = factory.CreateDbContext())
        {
            var movie = db.Movies.Single(m => m.Id == movieId);
            Assert.Equal("UpdatedHatano Yui", movie.MetaActresses);

            var actor = db.Actors.Include(a => a.Aliases).Single(a => a.Id == actorId);
            Assert.Equal("UpdatedHatano Yui", actor.DisplayName);
            Assert.Contains(actor.Aliases, a => a.Name == "Hatano Yui");
        }

        // Simulate nfo file write time having moved or being checked during rescan
        await Task.Delay(10);
        File.SetLastWriteTimeUtc(nfoPath, DateTime.UtcNow);

        // 2. Rescan: RefreshLocalMetadataIfNfoChangedAsync must NOT overwrite Javbuddy's MetaActresses
        var refreshResult = await client.RefreshLocalMetadataIfNfoChangedAsync(movieId);
        Assert.True(refreshResult.Success);

        using (var db = factory.CreateDbContext())
        {
            var movie = db.Movies.Single(m => m.Id == movieId);
            Assert.Equal("UpdatedHatano Yui", movie.MetaActresses);
        }

        // 3. Rescan: Detect conflicts
        var conflictResult = await service.CheckMovieNfoConflictAsync(movieId);
        Assert.True(conflictResult.HasConflict);
        Assert.Contains("Hatano Yui", conflictResult.ConflictDetails);
        Assert.Contains("UpdatedHatano Yui", conflictResult.ConflictDetails);

        using (var db = factory.CreateDbContext())
        {
            var movie = db.Movies.Single(m => m.Id == movieId);
            Assert.NotEqual(NfoDriftKind.None, movie.NfoDriftKind);
            Assert.NotNull(movie.NfoConflictDetails);
        }

        // 4. Update .nfo pushes Javbuddy's authoritative data to disk and clears the conflict
        var syncResult = await service.SyncMovieNfoAsync(movieId);
        Assert.Equal(1, syncResult.UpdatedCount);

        var updatedXml = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<name>UpdatedHatano Yui</name>", updatedXml);
        Assert.DoesNotContain("<name>Hatano Yui</name>", updatedXml);

        using (var db = factory.CreateDbContext())
        {
            var movie = db.Movies.Single(m => m.Id == movieId);
            Assert.Equal(NfoDriftKind.None, movie.NfoDriftKind);
            Assert.Null(movie.NfoConflictDetails);
        }
    }

    [Fact]
    public async Task GenerateProposedNfoAsync_ReplacesDisambiguatedActorName_RatherThanAppendingDuplicate()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("SSNI-999");
        var nfoPath = Path.Combine(folder, "SSNI-999.nfo");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>SSNI-999</title>
              <actor>
                <name>Hashimoto Arina2</name>
                <thumb>https://pics.r18.com/mono/actjpgs/hasimoto_arina.jpg</thumb>
                <role>Actress</role>
              </actor>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Arina", LastName = "Hashimoto" };
            db.Actors.Add(actor);
            var movie = new Movie { Code = "SSNI-999", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = actor.Id });
            db.SaveChanges();
        }

        var conflictCheck = await service.CheckMovieNfoConflictAsync(movieId);
        Assert.True(conflictCheck.HasConflict);
        Assert.Contains("Hashimoto Arina2", conflictCheck.ConflictDetails);
        Assert.Contains("Hashimoto Arina", conflictCheck.ConflictDetails);

        var proposed = await service.GenerateProposedNfoAsync(movieId);
        Assert.NotNull(proposed);
        Assert.Contains("<name>Hashimoto Arina</name>", proposed);
        Assert.DoesNotContain("<name>Hashimoto Arina2</name>", proposed);
        Assert.Contains("<thumb>https://pics.r18.com/mono/actjpgs/hasimoto_arina.jpg</thumb>", proposed);
        Assert.Contains("<role>Actress</role>", proposed);

        // Verify only 1 actor element exists in the proposed XML
        var doc = System.Xml.Linq.XDocument.Parse(proposed);
        Assert.Single(doc.Root!.Elements("actor"));
    }

    [Theory]
    [InlineData("Hashimoto Arina2")]
    [InlineData("Hashimoto Arina 2")]
    [InlineData("Hashimoto Arina (2)")]
    [InlineData("Hashimoto Arina_2")]
    [InlineData("Hashimoto Arina #2")]
    [InlineData("Hashimoto Arina（2）")]
    public void Matches_RecognizesDisambiguationSuffixes(string nfoName)
    {
        var matcher = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Hashimoto Arina", "HashimotoArina" };
        Assert.True(NfoActorMatching.Matches(nfoName, matcher));
    }

    [Fact]
    public void FindMatchingActorElements_SingleActorFallback_UsesTheOnlyOnDiskActor()
    {
        var root = System.Xml.Linq.XElement.Parse("<movie><actor><name>Unmatched Name</name></actor></movie>");
        var matcher = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Canonical Name" };

        var matching = NfoActorMatching.FindMatchingActorElements(root, matcher, linkedActorCount: 1);

        Assert.Single(matching);
        Assert.Equal("Unmatched Name", matching[0].Element("name")?.Value);
    }

    [Fact]
    public async Task CheckMovieNfoConflictAsync_DetectsFieldLevelDrift_ForEveryNonActorField()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("FLD-001");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>Old Title</title>
              <originaltitle>Old Original</originaltitle>
              <plot>Old plot.</plot>
              <director>Old Director</director>
              <maker>Old Studio</maker>
              <label>Old Label</label>
              <set><name>Old Series</name></set>
              <premiered>2020-01-01</premiered>
              <ratings><rating><value>4.00</value></rating></ratings>
              <votes>10</votes>
              <runtime>100</runtime>
              <genre>Drama</genre>
              <genre>Comedy</genre>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder, "FLD-001.nfo"), nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie
            {
                Code = "FLD-001",
                Status = MovieStatus.Got,
                MetaTitle = "New Title",
                MetaOriginalTitle = "New Original",
                MetaDescription = "New plot.",
                MetaDirector = "New Director",
                MetaStudio = "New Studio",
                MetaLabel = "New Label",
                MetaSeries = "New Series",
                MetaReleaseDate = new DateTime(2021, 6, 15),
                MetaRatingScore = 4.5,
                MetaRatingVotes = 20,
                MetaRuntimeMinutes = 110
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            AddGenreTags(db, movie, "Drama", "Romance");
            movieId = movie.Id;
        }

        var result = await service.CheckMovieNfoConflictAsync(movieId);

        Assert.True(result.HasConflict);
        Assert.NotNull(result.ConflictDetails);
        Assert.Contains("Title: .nfo has 'Old Title', canonical is 'New Title'", result.ConflictDetails);
        Assert.Contains("Original title: .nfo has 'Old Original', canonical is 'New Original'", result.ConflictDetails);
        Assert.Contains("Plot: .nfo has 'Old plot.', canonical is 'New plot.'", result.ConflictDetails);
        Assert.Contains("Director: .nfo has 'Old Director', canonical is 'New Director'", result.ConflictDetails);
        Assert.Contains("Studio: .nfo has 'Old Studio', canonical is 'New Studio'", result.ConflictDetails);
        Assert.Contains("Label: .nfo has 'Old Label', canonical is 'New Label'", result.ConflictDetails);
        Assert.Contains("Series: .nfo has 'Old Series', canonical is 'New Series'", result.ConflictDetails);
        Assert.Contains("Release date: .nfo has '2020-01-01', canonical is '2021-06-15'", result.ConflictDetails);
        Assert.Contains("Rating: .nfo has '4', canonical is '4.5'", result.ConflictDetails);
        Assert.Contains("Rating votes: .nfo has '10', canonical is '20'", result.ConflictDetails);
        Assert.Contains("Runtime: .nfo has '100' minutes, canonical is '110' minutes", result.ConflictDetails);
        Assert.Contains("Genres:", result.ConflictDetails);
    }

    [Fact]
    public async Task CheckMovieNfoConflictAsync_MatchingFields_NoConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("FLD-002");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>Same Title</title>
              <genre>Drama</genre>
              <genre>Comedy</genre>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder, "FLD-002.nfo"), nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie
            {
                Code = "FLD-002",
                Status = MovieStatus.Got,
                MetaTitle = "Same Title"
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            AddGenreTags(db, movie, "Comedy", "Drama");
            movieId = movie.Id;
        }

        var result = await service.CheckMovieNfoConflictAsync(movieId);

        Assert.False(result.HasConflict);
        Assert.Null(result.ConflictDetails);
    }

    [Fact]
    public async Task CheckMovieNfoConflictAsync_FieldEmptyOnOneSide_NoConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        // .nfo has no <plot>/<director>/<label> at all; Javbuddy has no MetaTitle yet.
        var folder = root.AddMovieFolder("FLD-003");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>Only Title</title>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder, "FLD-003.nfo"), nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie
            {
                Code = "FLD-003",
                Status = MovieStatus.Got,
                MetaDescription = "Javbuddy-only plot, .nfo has none."
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var result = await service.CheckMovieNfoConflictAsync(movieId);

        Assert.False(result.HasConflict);
        Assert.Null(result.ConflictDetails);
    }

    [Fact]
    public async Task GenerateProposedNfoAsync_ReconcilesScalarFieldAndGenres()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("FLD-004");
        var nfoPath = Path.Combine(folder, "FLD-004.nfo");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>Old Title</title>
              <genre>Drama</genre>
              <genre>Comedy</genre>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie
            {
                Code = "FLD-004",
                Status = MovieStatus.Got,
                MetaTitle = "New Title",
                NfoDriftKind = NfoDriftKind.ExternalEdit,
                NfoConflictDetails = "Conflict"
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            // Tags added out of alphabetical order deliberately — see the sort assertion below.
            AddGenreTags(db, movie, "Romance", "Action", "Comedy");
            movieId = movie.Id;
        }

        var proposed = await service.GenerateProposedNfoAsync(movieId);

        Assert.NotNull(proposed);
        var proposedDoc = System.Xml.Linq.XDocument.Parse(proposed);
        Assert.Equal("New Title", proposedDoc.Root!.Element("title")?.Value);
        // Tags were added in "Romance, Action, Comedy" order (not alphabetical) — the written
        // <genre> elements must still come out in deterministic sorted order regardless.
        Assert.Equal(
            new[] { "Action", "Comedy", "Romance" },
            proposedDoc.Root!.Elements("genre").Select(e => e.Value).ToArray());

        // On-disk file untouched.
        var diskXml = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<title>Old Title</title>", diskXml);
    }

    [Fact]
    public async Task CheckMovieNfoConflictAsync_TagNameContainingComma_MatchesSingleNfoGenre_NoFalseConflict()
    {
        // A canonical Tag name that itself contains a comma (e.g. "Nasty, hardcore") must compare
        // as one genre against a single matching <genre> element, not get split into "Nasty" and
        // "hardcore" pieces the way re-parsing MetaGenres's own comma-joined text would.
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("FLD-005");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>Same Title</title>
              <genre>Nasty, hardcore</genre>
            </movie>
            """;
        await File.WriteAllTextAsync(Path.Combine(folder, "FLD-005.nfo"), nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "FLD-005", Status = MovieStatus.Got, MetaTitle = "Same Title" };
            db.Movies.Add(movie);
            db.SaveChanges();
            AddGenreTags(db, movie, "Nasty, hardcore");
            movieId = movie.Id;
        }

        var result = await service.CheckMovieNfoConflictAsync(movieId);

        Assert.False(result.HasConflict);
        Assert.Null(result.ConflictDetails);
    }

    [Fact]
    public async Task GenerateProposedNfoAsync_TagNameContainingComma_WritesAsOneGenreElement()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("FLD-006");
        var nfoPath = Path.Combine(folder, "FLD-006.nfo");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>Same Title</title>
              <genre>Stale</genre>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "FLD-006", Status = MovieStatus.Got, MetaTitle = "Same Title" };
            db.Movies.Add(movie);
            db.SaveChanges();
            AddGenreTags(db, movie, "Nasty, hardcore", "VR");
            movieId = movie.Id;
        }

        var proposed = await service.GenerateProposedNfoAsync(movieId);

        Assert.NotNull(proposed);
        var proposedDoc = System.Xml.Linq.XDocument.Parse(proposed);
        Assert.Equal(
            new[] { "Nasty, hardcore", "VR" },
            proposedDoc.Root!.Elements("genre").Select(e => e.Value).ToArray());
    }

    [Fact]
    public async Task GenerateProposedNfoAsync_HierarchicalTag_OutputsDualGenreElements()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("FLD-007");
        var nfoPath = Path.Combine(folder, "FLD-007.nfo");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>Same Title</title>
              <genre>Stale</genre>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var parentTag = new Tag { Name = "Cosplay" };
            var childTag = new Tag { Name = "ram", ParentTag = parentTag };
            db.Tags.AddRange(parentTag, childTag);
            db.SaveChanges();

            var movie = new Movie { Code = "FLD-007", Status = MovieStatus.Got, MetaTitle = "Same Title" };
            db.Movies.Add(movie);
            db.SaveChanges();

            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = childTag.Id });
            db.SaveChanges();
            movieId = movie.Id;
        }

        var proposed = await service.GenerateProposedNfoAsync(movieId);

        Assert.NotNull(proposed);
        var proposedDoc = System.Xml.Linq.XDocument.Parse(proposed);
        Assert.Equal(
            new[] { "Cosplay", "Cosplay##ram" },
            proposedDoc.Root!.Elements("genre").Select(e => e.Value).ToArray());
    }

    [Fact]
    public async Task GenerateProposedNfoAsync_ReconcilesReleaseDateAndRuntime()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("FLD-005");
        var nfoPath = Path.Combine(folder, "FLD-005.nfo");
        var nfo = """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <releasedate>2020-01-01</releasedate>
              <runtime>90</runtime>
            </movie>
            """;
        await File.WriteAllTextAsync(nfoPath, nfo);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie
            {
                Code = "FLD-005",
                Status = MovieStatus.Got,
                MetaReleaseDate = new DateTime(2024, 6, 15),
                MetaRuntimeMinutes = 118,
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var proposed = await service.GenerateProposedNfoAsync(movieId);

        Assert.NotNull(proposed);
        var proposedDoc = XDocument.Parse(proposed);
        Assert.Equal("2024-06-15", proposedDoc.Root!.Element("releasedate")?.Value);
        Assert.Equal("118", proposedDoc.Root!.Element("runtime")?.Value);

        // On-disk file untouched — GenerateProposedNfoAsync only ever seeds a diff preview.
        var diskXml = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<runtime>90</runtime>", diskXml);
    }

    [Fact]
    public async Task GenerateProposedNfoAsync_DoesNotInventReleaseDateOrRuntimeElements()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("FLD-006");
        var nfoPath = Path.Combine(folder, "FLD-006.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie><title>T</title></movie>");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie
            {
                Code = "FLD-006",
                Status = MovieStatus.Got,
                MetaReleaseDate = new DateTime(2024, 6, 15),
                MetaRuntimeMinutes = 118,
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var proposed = await service.GenerateProposedNfoAsync(movieId);

        Assert.NotNull(proposed);
        var proposedDoc = XDocument.Parse(proposed);
        Assert.Null(proposedDoc.Root!.Element("releasedate"));
        Assert.Null(proposedDoc.Root!.Element("runtime"));
    }

    [Fact]
    public async Task CheckMovieMetadataConflictAsync_ScalarFieldDiffers_FlagsConflict_WithoutWritingToDisk()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        // The metadata editor modal never writes to the .nfo directly — Save only updates
        // the DB, so even a field whose .nfo element already has *some* value must still be
        // flagged here (unlike the lenient CheckMovieNfoConflictAsync, nothing will silently fix
        // this by writing the new value in).
        var folder = root.AddMovieFolder("STR-001");
        var nfoPath = Path.Combine(folder, "STR-001.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie><studio>Old Studio</studio></movie>");
        var originalXml = await File.ReadAllTextAsync(nfoPath);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "STR-001", Status = MovieStatus.Got, MetaStudio = "New Studio" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var result = await service.CheckMovieMetadataConflictAsync(movieId);

        Assert.True(result.HasConflict);
        Assert.Contains("Studio: .nfo has 'Old Studio', canonical is 'New Studio'", result.ConflictDetails);
        Assert.Equal(originalXml, await File.ReadAllTextAsync(nfoPath));

        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.NotEqual(NfoDriftKind.None, reloaded.NfoDriftKind);
        Assert.Null(reloaded.MediaNfoLastWriteUtc);
    }

    [Fact]
    public async Task CheckMovieMetadataConflictAsync_ElementPresentButBlank_FlagsConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("STR-002");
        await File.WriteAllTextAsync(Path.Combine(folder, "STR-002.nfo"), "<movie><director></director></movie>");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "STR-002", Status = MovieStatus.Got, MetaDirector = "New Director" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var result = await service.CheckMovieMetadataConflictAsync(movieId);

        Assert.True(result.HasConflict);
        Assert.Contains("Director: .nfo has '(none)', canonical is 'New Director'", result.ConflictDetails);
    }

    [Fact]
    public async Task CheckMovieMetadataConflictAsync_NoElementAtAll_FlagsConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("STR-003");
        await File.WriteAllTextAsync(Path.Combine(folder, "STR-003.nfo"), "<movie><title>T</title></movie>");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "STR-003", Status = MovieStatus.Got, MetaLabel = "New Label" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var result = await service.CheckMovieMetadataConflictAsync(movieId);

        Assert.True(result.HasConflict);
        Assert.Contains("Label: .nfo has '(none)', canonical is 'New Label'", result.ConflictDetails);
    }

    [Fact]
    public async Task CheckMovieMetadataConflictAsync_ReleaseDateAndRuntimeMissingFromNfo_FlagConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("STR-004");
        await File.WriteAllTextAsync(Path.Combine(folder, "STR-004.nfo"), "<movie><title>T</title></movie>");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie
            {
                Code = "STR-004",
                Status = MovieStatus.Got,
                MetaReleaseDate = new DateTime(2024, 6, 1),
                MetaRuntimeMinutes = 105,
            };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var result = await service.CheckMovieMetadataConflictAsync(movieId);

        Assert.True(result.HasConflict);
        Assert.Contains("Release date: .nfo has '(none)', canonical is '2024-06-01'", result.ConflictDetails);
        Assert.Contains("Runtime: .nfo has '(none)' minutes, canonical is '105' minutes", result.ConflictDetails);
    }

    [Fact]
    public async Task CheckMovieMetadataConflictAsync_ValuesAlreadyMatch_NoConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("STR-005");
        await File.WriteAllTextAsync(Path.Combine(folder, "STR-005.nfo"), "<movie><label>Matching</label></movie>");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "STR-005", Status = MovieStatus.Got, MetaLabel = "Matching" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var result = await service.CheckMovieMetadataConflictAsync(movieId);

        Assert.False(result.HasConflict);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var reloaded = await verifyDb.Movies.SingleAsync(m => m.Id == movieId);
        Assert.Equal(NfoDriftKind.None, reloaded.NfoDriftKind);
    }

    [Fact]
    public async Task CheckMovieMetadataConflictAsync_RatingDriftWithNoNfoElement_StaysLenient()
    {
        // Rating/rating votes/genres aren't editable through the metadata editor modal, so strict
        // matching must never apply to them — otherwise every movie whose .nfo simply doesn't
        // carry a <rating> would start showing a conflict the modal had nothing to do with.
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("STR-006");
        await File.WriteAllTextAsync(Path.Combine(folder, "STR-006.nfo"), "<movie><title>T</title></movie>");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "STR-006", Status = MovieStatus.Got, MetaRatingScore = 4.5, MetaRatingVotes = 20 };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var result = await service.CheckMovieMetadataConflictAsync(movieId);

        Assert.False(result.HasConflict);
    }

    [Fact]
    public async Task CheckMovieMetadataConflictAsync_MissingMovieStatus_NoOp()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("STR-007");
        await File.WriteAllTextAsync(Path.Combine(folder, "STR-007.nfo"), "<movie><label></label></movie>");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "STR-007", Status = MovieStatus.Missing, MetaLabel = "New Label" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var result = await service.CheckMovieMetadataConflictAsync(movieId);

        Assert.False(result.HasConflict);
    }

    [Fact]
    public async Task CheckMovieMetadataConflictAsync_NoLocalNfo_NoOp()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "STR-008", Status = MovieStatus.Got, MetaLabel = "New Label" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var result = await service.CheckMovieMetadataConflictAsync(movieId);

        Assert.False(result.HasConflict);
    }

    [Fact]
    public async Task SyncMovieMetadataToNfoAsync_UpdatesExistingElementFromMovie()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("SYN-001");
        var nfoPath = Path.Combine(folder, "SYN-001.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie><title>Old Title</title><director></director></movie>");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "SYN-001", Status = MovieStatus.Got, MetaTitle = "New Title", MetaDirector = "New Director" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var wrote = await service.SyncMovieMetadataToNfoAsync(movieId);

        Assert.True(wrote);
        var nfoXml = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<title>New Title</title>", nfoXml);
        // <director> exists but was blank on disk — ApplyFieldUpdates never invents a value for it.
        Assert.Contains("<director></director>", nfoXml);
        Assert.Equal([(NfoWriteTrigger.MetadataSync, "<movie><title>Old Title</title><director></director></movie>")], NfoHistory(factory, "SYN-001"));

        using var verifyDb = factory.CreateDbContext();
        var reloaded = verifyDb.Movies.Single(m => m.Id == movieId);
        Assert.Equal(File.GetLastWriteTimeUtc(nfoPath), reloaded.MediaNfoLastWriteUtc);
    }

    [Fact]
    public async Task SyncMovieMetadataToNfoAsync_NoLocalNfo_ReturnsFalse()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "SYN-002", Status = MovieStatus.Missing, MetaTitle = "New Title" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var wrote = await service.SyncMovieMetadataToNfoAsync(movieId);

        Assert.False(wrote);
    }

    [Fact]
    public async Task SyncMovieMetadataToNfoAsync_NothingChanged_ReturnsFalseAndLeavesFileUntouched()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        var folder = root.AddMovieFolder("SYN-003");
        var nfoPath = Path.Combine(folder, "SYN-003.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie><title>Same Title</title></movie>");
        var originalXml = await File.ReadAllTextAsync(nfoPath);

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "SYN-003", Status = MovieStatus.Got, MetaTitle = "Same Title" };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }

        var wrote = await service.SyncMovieMetadataToNfoAsync(movieId);

        Assert.False(wrote);
        Assert.Equal(originalXml, await File.ReadAllTextAsync(nfoPath));
        Assert.Empty(NfoHistory(factory, "SYN-003"));
    }

    [Fact]
    public async Task SyncMovieMetadataToNfoAsync_PreservesUnrelatedActorConflict()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);

        // ApplyFieldUpdates only ever touches the scalar descriptive fields — a pre-existing actor
        // mismatch is untouched by the title write below, but the recheck afterward must still
        // surface it rather than silently drop it.
        var folder = root.AddMovieFolder("SYN-004");
        var nfoPath = Path.Combine(folder, "SYN-004.nfo");
        await File.WriteAllTextAsync(nfoPath, "<movie><title>Old Title</title><actor><name>Someone Else</name></actor></movie>");

        int movieId;
        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var movie = new Movie { Code = "SYN-004", Status = MovieStatus.Got, MetaTitle = "New Title", MetaActresses = "Mikami Yua" };
            db.AddRange(actor, movie);
            db.SaveChanges();
            db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
            db.SaveChanges();
            movieId = movie.Id;
        }

        var wrote = await service.SyncMovieMetadataToNfoAsync(movieId);

        Assert.True(wrote);
        var nfoXml = await File.ReadAllTextAsync(nfoPath);
        Assert.Contains("<title>New Title</title>", nfoXml);

        using var verifyDb = factory.CreateDbContext();
        var reloaded = verifyDb.Movies.Single(m => m.Id == movieId);
        Assert.NotEqual(NfoDriftKind.None, reloaded.NfoDriftKind);
        Assert.Contains("Someone Else", reloaded.NfoConflictDetails);
    }

    // ---- Baseline, direction and fast skip, through the real service + filesystem ----

    private static async Task<(NfoSyncService Service, LocalLibraryClient Client, int MovieId, string NfoPath)> SeedDriftMovieAsync(
        TestDbContextFactory factory, TempRoot root, string title = "Title A", string nfoTitle = "Title A")
    {
        var client = CreateClientFor(factory, root);
        var service = new NfoSyncService(factory, client, NullLogger<NfoSyncService>.Instance);
        var folder = root.AddMovieFolder("DRF-001");
        var nfoPath = Path.Combine(folder, "DRF-001.nfo");
        await File.WriteAllTextAsync(nfoPath, $"<movie><title>{nfoTitle}</title><plot>Plot A</plot><actor><name>Mikami Yua</name></actor></movie>");

        using var db = factory.CreateDbContext();
        var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
        actor.Aliases.Add(new ActorAlias { Name = "Yua Mikami" });
        db.Actors.Add(actor);
        var movie = new Movie { Code = "DRF-001", Status = MovieStatus.Got, MetaTitle = title, MetaDescription = "Plot A" };
        db.Movies.Add(movie);
        db.SaveChanges();
        db.MovieActors.Add(new MovieActor { MovieId = movie.Id, ActorId = actor.Id });
        db.SaveChanges();
        return (service, client, movie.Id, nfoPath);
    }

    /// <summary>The movie's stored .nfo generations, newest first.</summary>
    private static List<(NfoWriteTrigger Trigger, string Content)> NfoHistory(TestDbContextFactory factory, string code)
    {
        using var db = factory.CreateDbContext();
        return db.NfoGenerations
            .Where(g => g.Movie.Code == code)
            .OrderByDescending(g => g.Id)
            .AsEnumerable()
            .Select(g => (g.Trigger, g.Content))
            .ToList();
    }

    private static Movie Reload(TestDbContextFactory factory, int movieId)
    {
        using var db = factory.CreateDbContext();
        return db.Movies.Single(m => m.Id == movieId);
    }

    private static void UpdateMovie(TestDbContextFactory factory, int movieId, Action<Movie> update)
    {
        using var db = factory.CreateDbContext();
        update(db.Movies.Single(m => m.Id == movieId));
        db.SaveChanges();
    }

    [Fact]
    public async Task DriftCheck_JavbuddyEditAfterAgreement_IsJavbuddyChanged_WithoutReReadingTheUnchangedNfo()
    {
        if (OperatingSystem.IsWindows()) return;
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var (service, _, movieId, nfoPath) = await SeedDriftMovieAsync(factory, root);

        Assert.False((await service.CheckMovieNfoConflictAsync(movieId)).HasConflict);
        var baselined = Reload(factory, movieId);
        Assert.NotNull(baselined.NfoBaselineJson);
        Assert.Equal(File.GetLastWriteTimeUtc(nfoPath), baselined.NfoBaselineLastWriteUtc);
        Assert.Equal(new FileInfo(nfoPath).Length, baselined.NfoBaselineSize);

        // Unreadable from here on: any attempt to re-read the unchanged file would surface as
        // Unreadable, so a correct direction proves the cached disk values were used.
        File.SetUnixFileMode(nfoPath, UnixFileMode.None);
        try
        {
            UpdateMovie(factory, movieId, m => m.MetaTitle = "Title B");

            var result = await service.CheckMovieNfoConflictAsync(movieId);

            Assert.True(result.HasConflict);
            var reloaded = Reload(factory, movieId);
            Assert.Equal(NfoDriftKind.JavbuddyChanged, reloaded.NfoDriftKind);
            Assert.Equal("[Javbuddy changed] Title: .nfo has 'Title A', canonical is 'Title B'", reloaded.NfoConflictDetails);
        }
        finally
        {
            File.SetUnixFileMode(nfoPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task DriftCheck_NfoEditedAfterAgreement_IsReParsedAndExternalEdit()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var (service, client, movieId, nfoPath) = await SeedDriftMovieAsync(factory, root);
        await service.CheckMovieNfoConflictAsync(movieId);

        // A hand edit through Edit .nfo is still an edit to the file, not to Javbuddy's data.
        Assert.NotNull(await client.SaveRawNfoAsync("DRF-001", "<movie><title>Title C</title><plot>Plot A</plot><actor><name>Mikami Yua</name></actor></movie>"));
        await service.CheckMovieNfoConflictAsync(movieId);

        var reloaded = Reload(factory, movieId);
        Assert.Equal(NfoDriftKind.ExternalEdit, reloaded.NfoDriftKind);
        Assert.Equal("[External edit] Title: .nfo has 'Title C', canonical is 'Title A'", reloaded.NfoConflictDetails);
        Assert.Equal(File.GetLastWriteTimeUtc(nfoPath), reloaded.NfoBaselineLastWriteUtc);
    }

    [Fact]
    public async Task DetectAllMovieConflictsAsync_FirstCheckOfAnAlreadyDifferingMovie_IsExternalEdit()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var (service, _, movieId, _) = await SeedDriftMovieAsync(factory, root, title: "Title B", nfoTitle: "Title A");

        var result = await service.DetectAllMovieConflictsAsync();

        Assert.Equal(1, result.ConflictsFoundCount);
        Assert.Equal(NfoDriftKind.ExternalEdit, Reload(factory, movieId).NfoDriftKind);
    }

    [Fact]
    public async Task DriftCheck_InvalidXml_IsUnreadable_AndKeepsTheBaseline()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var (service, _, movieId, nfoPath) = await SeedDriftMovieAsync(factory, root);
        await service.CheckMovieNfoConflictAsync(movieId);
        var baselineJson = Reload(factory, movieId).NfoBaselineJson;

        await File.WriteAllTextAsync(nfoPath, "<movie><title>broken");
        await service.CheckMovieNfoConflictAsync(movieId);

        var reloaded = Reload(factory, movieId);
        Assert.Equal(NfoDriftKind.Unreadable, reloaded.NfoDriftKind);
        Assert.StartsWith("Error reading .nfo:", reloaded.NfoConflictDetails);
        Assert.Equal(baselineJson, reloaded.NfoBaselineJson);
    }

    [Fact]
    public async Task SyncMovieNfoAsync_ReportsDriftThePartialWriteLeftBehind_AsJavbuddyChanged()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var (service, _, movieId, nfoPath) = await SeedDriftMovieAsync(factory, root);
        await service.CheckMovieNfoConflictAsync(movieId);

        // Javbuddy renames the actor and edits the title; SyncMovieNfoAsync only syncs actors.
        using (var db = factory.CreateDbContext())
        {
            var actor = db.Actors.Single();
            actor.LastName = "Mikami2";
            db.ActorAliases.Add(new ActorAlias { ActorId = actor.Id, Name = "Mikami Yua" });
            db.Movies.Single(m => m.Id == movieId).MetaTitle = "Title B";
            db.SaveChanges();
        }

        var sync = await service.SyncMovieNfoAsync(movieId);

        Assert.Equal(1, sync.UpdatedCount);
        Assert.Contains("<name>Mikami2 Yua</name>", await File.ReadAllTextAsync(nfoPath));
        var reloaded = Reload(factory, movieId);
        Assert.Equal(NfoDriftKind.JavbuddyChanged, reloaded.NfoDriftKind);
        Assert.Equal("[Javbuddy changed] Title: .nfo has 'Title A', canonical is 'Title B'", reloaded.NfoConflictDetails);
        Assert.Equal(File.GetLastWriteTimeUtc(nfoPath), reloaded.NfoBaselineLastWriteUtc);
    }

    // ---- Bulk push of Javbuddy metadata to drifting .nfo files ----

    [Fact]
    public async Task PushNfoDriftAsync_WritesJavbuddyChangedMovies_KeepingFormattingAndHistory()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var (service, _, movieId, nfoPath) = await SeedDriftMovieAsync(factory, root);
        await File.WriteAllTextAsync(nfoPath, "<movie>\n  <title>Title A</title>\n  <plot>Plot A</plot>\n  <actor><name>Mikami Yua</name></actor>\n</movie>");
        await service.CheckMovieNfoConflictAsync(movieId);
        UpdateMovie(factory, movieId, m => m.MetaTitle = "Title B");
        await service.CheckMovieNfoConflictAsync(movieId);
        Assert.Equal(NfoDriftKind.JavbuddyChanged, Reload(factory, movieId).NfoDriftKind);

        var result = await service.PushNfoDriftAsync([movieId], includeExternal: false);

        Assert.Equal(new NfoDriftPushResult(Written: 1, SkippedExternal: 0, Failed: 0, StillDrifting: 0), result);
        Assert.Equal("<movie>\n  <title>Title B</title>\n  <plot>Plot A</plot>\n  <actor><name>Mikami Yua</name></actor>\n</movie>", await File.ReadAllTextAsync(nfoPath));
        var generation = Assert.Single(NfoHistory(factory, "DRF-001"));
        Assert.Equal(NfoWriteTrigger.DriftPush, generation.Trigger);
        Assert.Contains("<title>Title A</title>", generation.Content);
        var reloaded = Reload(factory, movieId);
        Assert.Equal(NfoDriftKind.None, reloaded.NfoDriftKind);
        Assert.Equal(File.GetLastWriteTimeUtc(nfoPath), reloaded.NfoBaselineLastWriteUtc);
    }

    [Fact]
    public async Task PushNfoDriftAsync_LeavesOutsideEditsAlone_UnlessIncluded()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var (service, client, movieId, nfoPath) = await SeedDriftMovieAsync(factory, root);
        await service.CheckMovieNfoConflictAsync(movieId);
        const string edited = "<movie><title>Title C</title><plot>Plot A</plot><actor><name>Mikami Yua</name></actor></movie>";
        Assert.NotNull(await client.SaveRawNfoAsync("DRF-001", edited));
        await service.CheckMovieNfoConflictAsync(movieId);
        Assert.Equal(NfoDriftKind.ExternalEdit, Reload(factory, movieId).NfoDriftKind);

        var skipped = await service.PushNfoDriftAsync([movieId], includeExternal: false);

        Assert.Equal(new NfoDriftPushResult(0, SkippedExternal: 1, 0, 0), skipped);
        Assert.Equal(edited, await File.ReadAllTextAsync(nfoPath));
        Assert.Empty(NfoHistory(factory, "DRF-001"));

        var overwritten = await service.PushNfoDriftAsync([movieId], includeExternal: true);

        Assert.Equal(new NfoDriftPushResult(Written: 1, 0, 0, 0), overwritten);
        Assert.Contains("<title>Title A</title>", await File.ReadAllTextAsync(nfoPath));
        Assert.Equal([(NfoWriteTrigger.DriftPush, edited)], NfoHistory(factory, "DRF-001"));
        Assert.Equal(NfoDriftKind.None, Reload(factory, movieId).NfoDriftKind);
    }

    [Fact]
    public async Task PushNfoDriftAsync_DriftItCannotWrite_IsReportedAsStillDrifting_WithoutTouchingTheFile()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var (service, _, movieId, nfoPath) = await SeedDriftMovieAsync(factory, root);
        // No <director> element: the metadata editor's strict check flags it, but the proposal
        // never invents an element, so there's nothing a push can write.
        UpdateMovie(factory, movieId, m => m.MetaDirector = "Someone");
        await service.CheckMovieMetadataConflictAsync(movieId);
        Assert.Equal(NfoDriftKind.JavbuddyChanged, Reload(factory, movieId).NfoDriftKind);
        var before = await File.ReadAllTextAsync(nfoPath);

        var result = await service.PushNfoDriftAsync([movieId], includeExternal: false);

        Assert.Equal(new NfoDriftPushResult(0, 0, 0, StillDrifting: 1), result);
        Assert.Equal(before, await File.ReadAllTextAsync(nfoPath));
        Assert.Empty(NfoHistory(factory, "DRF-001"));
        Assert.Equal(NfoDriftKind.JavbuddyChanged, Reload(factory, movieId).NfoDriftKind);
    }

    [Fact]
    public async Task PushNfoDriftAsync_IgnoresMoviesWithoutWritableDrift_AndCountsReadOnlyAsFailed()
    {
        using var factory = new TestDbContextFactory();
        using var root = new TempRoot();
        var (service, _, movieId, nfoPath) = await SeedDriftMovieAsync(factory, root);
        await service.CheckMovieNfoConflictAsync(movieId);

        // No drift: nothing to do, not counted anywhere.
        Assert.Equal(new NfoDriftPushResult(0, 0, 0, 0), await service.PushNfoDriftAsync([movieId], includeExternal: true));

        UpdateMovie(factory, movieId, m => m.NfoDriftKind = NfoDriftKind.Unreadable);
        Assert.Equal(new NfoDriftPushResult(0, 0, 0, 0), await service.PushNfoDriftAsync([movieId], includeExternal: true));

        UpdateMovie(factory, movieId, m => { m.MetaTitle = "Title B"; m.NfoDriftKind = NfoDriftKind.JavbuddyChanged; });
        new FileInfo(nfoPath).IsReadOnly = true;
        try
        {
            Assert.Equal(new NfoDriftPushResult(0, 0, Failed: 1, 0), await service.PushNfoDriftAsync([movieId], includeExternal: false));
            Assert.Contains("<title>Title A</title>", await File.ReadAllTextAsync(nfoPath));
        }
        finally
        {
            new FileInfo(nfoPath).IsReadOnly = false;
        }
    }
}
