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
        jellyfinClient.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>()).Returns(new List<string> { "Movies" });
        jellyfinClient.LookupInSelectedLibrariesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
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
    public async Task RunAsync_NoLibrariesSelected_ReportsNoProgress()
    {
        using var factory = new TestDbContextFactory();
        var jellyfinClient = Substitute.For<IJellyfinClient>();
        jellyfinClient.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);
        jellyfinClient.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>()).Returns(new List<string>());

        var task = new JellyfinLinkSyncTask(jellyfinClient, factory, EmptyConfiguration());
        var progress = new RecordingProgress();

        var summary = await task.RunAsync(CancellationToken.None, progress);

        Assert.Empty(progress.Reports);
        Assert.Equal("skipped — no libraries selected", summary);
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
        jellyfinClient.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>()).Returns(new List<string> { "Movies" });
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
        jellyfinClient.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>()).Returns(new List<string> { "Movies" });
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
        jellyfinClient.GetSelectedLibraryNamesAsync(Arg.Any<CancellationToken>()).Returns(new List<string> { "Movies" });
        jellyfinClient.LookupInSelectedLibrariesAsync("AAA-001", Arg.Any<CancellationToken>())
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
            Assert.Equal(MovieStatus.Got, movie.Status);
            Assert.Equal("jf-1", movie.JellyfinItemId);
        }
    }
}

