using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.MediaServer;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Javbuddy.Tests.Services.Tasks;

/// <summary>Coverage for progress reporting added so System &gt; Tasks (and the sidebar's
/// active-task indicator) show live "checked X / Y" progress instead of a silent
/// "running" row for the whole sync.</summary>
public class JellyfinLinkSyncTaskTests
{
    private sealed class RecordingProgress : IProgress<TaskProgress>
    {
        public List<TaskProgress> Reports { get; } = new();
        public void Report(TaskProgress value) => Reports.Add(value);
    }

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    [Fact]
    public async Task RunAsync_ReportsProgressForEachMovieChecked()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.AddRange(
                new Movie { Code = "AAA-001" },
                new Movie { Code = "AAA-002" },
                new Movie { Code = "AAA-003" });
            await db.SaveChangesAsync();
        }

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(true, "ok"));
        jellyfinClient.LookupMovieAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaServerLookupResult(true, new List<MediaServerItemDto>(), null));

        var task = new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration());
        var progress = new RecordingProgress();

        await task.RunAsync(CancellationToken.None, progress);

        Assert.Equal(3, progress.Reports.Count);
        Assert.Equal(new[] { 1, 2, 3 }, progress.Reports.Select(r => r.Current));
        Assert.All(progress.Reports, r => Assert.Equal(3, r.Total));
        Assert.All(progress.Reports, r => Assert.Equal("Checking Jellyfin", r.Stage));
    }

    [Fact]
    public async Task RunAsync_JellyfinDisabled_ReturnsSkippedSummary()
    {
        using var factory = new TestDbContextFactory();
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(false);

        var task = new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration());
        var progress = new RecordingProgress();

        var summary = await task.RunAsync(CancellationToken.None, progress);

        Assert.Empty(progress.Reports);
        Assert.Equal("skipped — Jellyfin integration is disabled", summary);
    }

    [Fact]
    public async Task RunAsync_LinksUnlinkedActors_WhenFoundInJellyfin()
    {
        using var factory = new TestDbContextFactory();
        int actor1Id;
        int actor2Id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor1 = new Actor { FirstName = "Mikami Yua", JellyfinPersonId = null };
            var actor2 = new Actor { FirstName = "Remu", JellyfinPersonId = "existing-id" };
            db.Actors.AddRange(actor1, actor2);
            await db.SaveChangesAsync();
            actor1Id = actor1.Id;
            actor2Id = actor2.Id;
        }

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(true, "ok"));
        jellyfinClient.LookupPersonAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(new JellyfinPersonDto { Id = "jf-person-123", Name = "Mikami Yua" });

        var task = new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration());
        var progress = new RecordingProgress();

        var summary = await task.RunAsync(CancellationToken.None, progress);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var updatedActor1 = await db.Actors.FindAsync(actor1Id);
            var updatedActor2 = await db.Actors.FindAsync(actor2Id);
            Assert.NotNull(updatedActor1);
            Assert.Equal("jf-person-123", updatedActor1.JellyfinPersonId);
            Assert.NotNull(updatedActor2);
            Assert.Equal("existing-id", updatedActor2.JellyfinPersonId);

            var settings = await db.JellyfinSettings.FirstOrDefaultAsync();
            Assert.NotNull(settings);
            Assert.Equal(1, settings.LastLinkSyncChecked);
            Assert.Equal(1, settings.LastLinkSyncMatched);
        }

        Assert.NotNull(summary);
        Assert.Contains("actors: 1/1", summary);
        await jellyfinClient.DidNotReceive().LookupPersonAsync("Remu", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_StoresThePersonsServerId_WhenLinkingAnActor()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Mikami Yua" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(true, "ok"));
        jellyfinClient.LookupPersonAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(new JellyfinPersonDto { Id = "jf-person-123", ServerId = "server-from-person" });

        await new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration()).RunAsync(CancellationToken.None, new RecordingProgress());

        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal("server-from-person", (await verify.Actors.FindAsync(actorId))!.JellyfinServerId);
        await jellyfinClient.DidNotReceive().GetServerIdAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_FallsBackToTheServersId_WhenThePersonHasNoServerId()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Mikami Yua" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(true, "ok"));
        jellyfinClient.GetServerIdAsync(Arg.Any<CancellationToken>()).Returns("server-from-system-info");
        jellyfinClient.LookupPersonAsync("Mikami Yua", Arg.Any<CancellationToken>())
            .Returns(new JellyfinPersonDto { Id = "jf-person-123" });

        await new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration()).RunAsync(CancellationToken.None, new RecordingProgress());

        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal("server-from-system-info", (await verify.Actors.FindAsync(actorId))!.JellyfinServerId);
    }

    [Fact]
    public async Task RunAsync_BackfillsTheServerId_OfLinkedActorsMissingOne_WithASingleServerIdRead()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.AddRange(
                new Actor { FirstName = "A", JellyfinPersonId = "p-a" },
                new Actor { FirstName = "B", JellyfinPersonId = "p-b" },
                new Actor { FirstName = "C", JellyfinPersonId = "p-c", JellyfinServerId = "kept-server" },
                new Actor { FirstName = "D" });
            await db.SaveChangesAsync();
        }

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(true, "ok"));
        jellyfinClient.GetServerIdAsync(Arg.Any<CancellationToken>()).Returns("server-1");

        await new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration()).RunAsync(CancellationToken.None, new RecordingProgress());

        await using var verify = await factory.CreateDbContextAsync();
        var byName = await verify.Actors.ToDictionaryAsync(a => a.FirstName!, a => a.JellyfinServerId);
        Assert.Equal("server-1", byName["A"]);
        Assert.Equal("server-1", byName["B"]);
        Assert.Equal("kept-server", byName["C"]);
        Assert.Null(byName["D"]);
        await jellyfinClient.Received(1).GetServerIdAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_DoesNotReadTheServerId_WhenNoLinkedActorNeedsOne()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { FirstName = "C", JellyfinPersonId = "p-c", JellyfinServerId = "s" });
            await db.SaveChangesAsync();
        }

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(true, "ok"));

        await new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration()).RunAsync(CancellationToken.None, new RecordingProgress());

        await jellyfinClient.DidNotReceive().GetServerIdAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MatchesActorByAlias_WhenDisplayNameNotFound()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Primary Name"
            };
            actor.Aliases.Add(new ActorAlias { Name = "Known Alias" });
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(true, "ok"));
        jellyfinClient.LookupPersonAsync("Primary Name", Arg.Any<CancellationToken>()).Returns((JellyfinPersonDto?)null);
        jellyfinClient.LookupPersonAsync("Known Alias", Arg.Any<CancellationToken>())
            .Returns(new JellyfinPersonDto { Id = "jf-alias-456", Name = "Known Alias" });

        var task = new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration());
        var progress = new RecordingProgress();

        await task.RunAsync(CancellationToken.None, progress);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var updated = await db.Actors.FindAsync(actorId);
            Assert.NotNull(updated);
            Assert.Equal("jf-alias-456", updated.JellyfinPersonId);
        }
    }

    [Fact]
    public async Task RunAsync_WhenMoviesMatched_NotifiesMovieChangeNotifier()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "AAA-001", Status = MovieStatus.Missing });
            await db.SaveChangesAsync();
        }

        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(true, "ok"));
        jellyfinClient.LookupMovieAsync("AAA-001", Arg.Any<CancellationToken>())
            .Returns(new MediaServerLookupResult(true, new List<MediaServerItemDto>
            {
                new() { Id = "jf-1", Name = "AAA-001 Match", ServerId = "srv-1" }
            }, null));

        var notifier = new MovieChangeNotifier();
        var notified = false;
        notifier.Changed += () => notified = true;

        var task = new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration(), notifier);
        var progress = new RecordingProgress();

        await task.RunAsync(CancellationToken.None, progress);

        Assert.True(notified);
        await using (var verifyDb = await factory.CreateDbContextAsync())
        {
            var movie = await verifyDb.Movies.SingleAsync();
            Assert.Equal(MovieStatus.Missing, movie.Status); // a link never decides ownership
            Assert.Equal("jf-1", movie.JellyfinItemId);
        }
    }

    [Fact]
    public async Task RunAsync_JellyfinUnreachable_SkipsWithoutLookingAnythingUp()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Code = "AAA-001" });
            await db.SaveChangesAsync();
        }
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(false, "timed out"));

        var summary = await new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration()).RunAsync(CancellationToken.None, new RecordingProgress());

        Assert.Contains("not reachable", summary);
        await jellyfinClient.DidNotReceive().LookupMovieAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ConsecutiveLookupFailures_StopTheRunAndSkipActors()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 1; i <= 6; i++) db.Movies.Add(new Movie { Code = $"AAA-00{i}" });
            db.Actors.Add(new Actor { FirstName = "Yua", LastName = "Mikami" });
            await db.SaveChangesAsync();
        }
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(true, "ok"));
        jellyfinClient.LookupMovieAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaServerLookupResult(false, null, "timed out"));

        var summary = await new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration()).RunAsync(CancellationToken.None, new RecordingProgress());

        Assert.Contains("stopped after 3 consecutive failed lookups", summary);
        await jellyfinClient.Received(3).LookupMovieAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await jellyfinClient.DidNotReceive().LookupPersonAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ASuccessfulLookupResetsTheFailureStreak()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 1; i <= 6; i++) db.Movies.Add(new Movie { Code = $"AAA-00{i}" });
            await db.SaveChangesAsync();
        }
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(true, "ok"));
        var fail = new MediaServerLookupResult(false, null, "x");
        var ok = new MediaServerLookupResult(true, new List<MediaServerItemDto>(), null);
        jellyfinClient.LookupMovieAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(fail, fail, ok, fail, fail, ok);

        var summary = await new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration()).RunAsync(CancellationToken.None, new RecordingProgress());

        Assert.DoesNotContain("stopped", summary);
        await jellyfinClient.Received(6).LookupMovieAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NeverLooksUpAnUnidentifiedPerformer_InJellyfin()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "AAA-001" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            db.Actors.Add(new Actor { FirstName = "Redhead", LastName = "AAA-001", PlaceholderMovieId = movie.Id });
            await db.SaveChangesAsync();
        }
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(new MediaServerTestResult(true, "ok"));
        jellyfinClient.LookupMovieAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MediaServerLookupResult(true, new List<MediaServerItemDto>(), null));

        await new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration()).RunAsync(CancellationToken.None, new RecordingProgress());

        await jellyfinClient.DidNotReceive().LookupPersonAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
