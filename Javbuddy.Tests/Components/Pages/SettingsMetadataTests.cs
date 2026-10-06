using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.ActorEnrichment;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Services.MinnanoAv;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.MovieDiscovery;
using Javbuddy.Services.R18Dev;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Tasks;
using Javbuddy.Services.Trickplay;
using Javbuddy.Services.Warashi;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class SettingsMetadataTests : BunitContext
{
    private readonly IConnectionStatusService connectionStatusService = Substitute.For<IConnectionStatusService>();

    public SettingsMetadataTests()
    {
        connectionStatusService.GetLocalLibraryStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new ConnectionStatus("locallibrary", false, false, "Not configured"));
        Services.AddSingleton(connectionStatusService);

        Services.AddSingleton<ILogger<BackgroundJobRunner>>(NullLogger<BackgroundJobRunner>.Instance);
        Services.AddSingleton<BackgroundJobRunner>();
        Services.AddSingleton<ScheduledTaskLauncher>();
        Services.AddSingleton<ActorBatchEnrichmentLauncher>();

        var trickplaySettings = Substitute.For<ITrickplaySettingsService>();
        trickplaySettings.GetEffectiveAsync(Arg.Any<CancellationToken>()).Returns(new TrickplaySettings());
        Services.AddSingleton(trickplaySettings);
    }

    private static TaskActivityTracker NewTaskActivityTracker() =>
        new(new ScheduledTaskChangeNotifier(), TimeProvider.System);

    [Fact]
    public void RendersMoviesAndActorsSectionsWithStatusTiles()
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);

        var dumpStore = Substitute.For<IR18DevDumpStore>();
        dumpStore.IsAvailable().Returns(false);
        Services.AddSingleton(dumpStore);

        var configuration = new ConfigurationBuilder().Build();
        Services.AddSingleton<IConfiguration>(configuration);

        Services.AddSingleton<IWarashiSettingsService>(new WarashiSettingsService(factory));
        Services.AddSingleton<IMinnanoAvSettingsService>(new MinnanoAvSettingsService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        Services.AddSingleton<IDiscoverySettingsService>(new DiscoverySettingsService(TwoSources(), factory));
        Services.AddSingleton<IMediaInfoSettingsService>(new MediaInfoSettingsService(factory));
        Services.AddSingleton(NewTaskActivityTracker());

        var cut = Render<SettingsMetadata>();

        // Verify section titles
        var h2s = cut.FindAll("h2.metadata-section-title");
        Assert.Equal(["Movies", "Actors", "Discovery", "Sources"], h2s.Select(h => h.TextContent));

        // r18.dev is a shared source, not a Movies tile; Discovery lists the studio sources.
        var sectionOf = (string tile) => cut.FindAll("section.metadata-section").Single(sec => sec.TextContent.Contains(tile)).QuerySelector("h2")!.TextContent;
        Assert.Equal("Sources", sectionOf("r18.dev"));
        Assert.Equal("Discovery", sectionOf("Studio sources"));
        Assert.Contains("2 of 2 enabled", cut.Markup);

        // Verify StatusTiles
        Assert.Contains("Local Library", cut.Markup);
        Assert.Contains("r18.dev", cut.Markup);
        Assert.Contains("MediaInfo", cut.Markup);
        Assert.Contains("Generating for new files", cut.Markup);
        Assert.Contains("WAPdB (warashi-asian-pornstars.fr)", cut.Markup);
        Assert.Contains("minnano-av.com", cut.Markup);
    }

    [Fact]
    public void LocalLibraryTile_ShowsStatusFromService()
    {
        connectionStatusService.GetLocalLibraryStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new ConnectionStatus("locallibrary", true, true, "2 root paths configured"));
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        var dumpStore = Substitute.For<IR18DevDumpStore>();
        Services.AddSingleton(dumpStore);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddSingleton<IWarashiSettingsService>(new WarashiSettingsService(factory));
        Services.AddSingleton<IMinnanoAvSettingsService>(new MinnanoAvSettingsService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        Services.AddSingleton<IDiscoverySettingsService>(new DiscoverySettingsService(TwoSources(), factory));
        Services.AddSingleton<IMediaInfoSettingsService>(new MediaInfoSettingsService(factory));
        Services.AddSingleton(NewTaskActivityTracker());

        var cut = Render<SettingsMetadata>();

        var moviesSection = cut.FindAll("section.metadata-section")[0];
        var tile = moviesSection.QuerySelectorAll(".status-tile").Single(t => t.TextContent.Contains("Local Library"));
        Assert.Contains("2 root paths configured", tile.TextContent);
        Assert.Contains("ENV configured", tile.TextContent);
    }

    [Fact]
    public void OpenMediaInfoDrawer_ShowsSettingsWithoutRescanButton()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            db.MediaInfoSettings.Add(new MediaInfoSettings { Enabled = true });
            db.SaveChanges();
        }

        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);

        var dumpStore = Substitute.For<IR18DevDumpStore>();
        Services.AddSingleton(dumpStore);

        var configuration = new ConfigurationBuilder().Build();
        Services.AddSingleton<IConfiguration>(configuration);

        Services.AddSingleton<IWarashiSettingsService>(new WarashiSettingsService(factory));
        Services.AddSingleton<IMinnanoAvSettingsService>(new MinnanoAvSettingsService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        Services.AddSingleton<IDiscoverySettingsService>(new DiscoverySettingsService(TwoSources(), factory));
        Services.AddSingleton<IMediaInfoSettingsService>(new MediaInfoSettingsService(factory));
        Services.AddSingleton(NewTaskActivityTracker());

        var cut = Render<SettingsMetadata>();

        // Click the MediaInfo tile to open drawer
        var mediaInfoTile = cut.FindAll(".status-tile").First(t => t.TextContent.Contains("MediaInfo"));
        mediaInfoTile.Click();

        // Verify drawer contents
        Assert.Contains("Enable MediaInfo probing", cut.Markup);
        Assert.Contains("Save", cut.Markup);
        Assert.DoesNotContain("Rescan library now", cut.Markup);
        Assert.DoesNotContain("The button below runs a full library rescan", cut.Markup);
    }

    [Fact]
    public void OpenWarashiDrawer_ShowsSettings()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            db.WarashiSettings.Add(new WarashiSettings { Enabled = true, RequestDelayMs = 750 });
            db.SaveChanges();
        }

        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);

        var dumpStore = Substitute.For<IR18DevDumpStore>();
        Services.AddSingleton(dumpStore);

        var configuration = new ConfigurationBuilder().Build();
        Services.AddSingleton<IConfiguration>(configuration);

        Services.AddSingleton<IWarashiSettingsService>(new WarashiSettingsService(factory));
        Services.AddSingleton<IMinnanoAvSettingsService>(new MinnanoAvSettingsService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        Services.AddSingleton<IDiscoverySettingsService>(new DiscoverySettingsService(TwoSources(), factory));
        Services.AddSingleton<IMediaInfoSettingsService>(new MediaInfoSettingsService(factory));
        Services.AddSingleton(NewTaskActivityTracker());

        var cut = Render<SettingsMetadata>();

        // Click the WAPdB tile to open drawer
        var warashiTile = cut.FindAll(".status-tile").First(t => t.TextContent.Contains("WAPdB"));
        warashiTile.Click();

        // Verify drawer contents
        Assert.Contains("Warashi Asian Pornstars Database (WAPdB)", cut.Markup);
        Assert.Contains("Enable the WAPdB metadata provider", cut.Markup);
        Assert.DoesNotContain("Search & Import Actor", cut.Markup);
    }

    [Fact]
    public void OpenMinnanoAvDrawer_ShowsSettings()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            db.MinnanoAvSettings.Add(new MinnanoAvSettings { Enabled = true, RequestDelayMs = 750 });
            db.SaveChanges();
        }

        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);

        var dumpStore = Substitute.For<IR18DevDumpStore>();
        Services.AddSingleton(dumpStore);

        var configuration = new ConfigurationBuilder().Build();
        Services.AddSingleton<IConfiguration>(configuration);

        Services.AddSingleton<IWarashiSettingsService>(new WarashiSettingsService(factory));
        Services.AddSingleton<IMinnanoAvSettingsService>(new MinnanoAvSettingsService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        Services.AddSingleton<IDiscoverySettingsService>(new DiscoverySettingsService(TwoSources(), factory));
        Services.AddSingleton<IMediaInfoSettingsService>(new MediaInfoSettingsService(factory));
        Services.AddSingleton(NewTaskActivityTracker());

        var cut = Render<SettingsMetadata>();

        // Click the minnano-av.com tile to open its drawer
        var minnanoAvTile = cut.FindAll(".status-tile").First(t => t.TextContent.Contains("minnano-av.com"));
        minnanoAvTile.Click();

        // Verify drawer contents
        Assert.Contains("Enable the minnano-av.com metadata provider", cut.Markup);
    }

    [Fact]
    public async Task MinnanoAvBatchEnrichNow_RunsDetachedAndReportsProgressViaTaskActivityTracker_EvenAfterPageIsDisposed()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            db.MinnanoAvSettings.Add(new MinnanoAvSettings { Enabled = true, RequestDelayMs = 750 });
            db.SaveChanges();
        }

        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton(Substitute.For<IR18DevDumpStore>());
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddSingleton<IWarashiSettingsService>(new WarashiSettingsService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        Services.AddSingleton<IDiscoverySettingsService>(new DiscoverySettingsService(TwoSources(), factory));
        Services.AddSingleton<IMediaInfoSettingsService>(new MediaInfoSettingsService(factory));

        var minnanoAvSettingsService = Substitute.For<IMinnanoAvSettingsService>();
        minnanoAvSettingsService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new MinnanoAvSettings { Enabled = true, RequestDelayMs = 750 });
        minnanoAvSettingsService.SaveAsync(Arg.Any<MinnanoAvSettings>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        minnanoAvSettingsService.SaveEnrichmentOutcomeAsync(Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        Services.AddSingleton(minnanoAvSettingsService);

        var tcs = new TaskCompletionSource<ActorBatchEnrichmentResult>();
        var actorEnrichment = Substitute.For<IActorEnrichmentService>();
        actorEnrichment
            .EnrichAllAsync(Arg.Any<ActorEnrichmentOptions>(), Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.Arg<IProgress<TaskProgress>>().Report(new TaskProgress(14, 150, "Mikami Yua"));
                return tcs.Task;
            });
        Services.AddSingleton(actorEnrichment);

        // The detached batch task (see below) isn't driven by this component's own render loop,
        // so wait on the tracker's change notifications directly instead of bUnit's render-tied
        // WaitForAssertion — same pattern TaskActivityTrackerTests uses.
        var notifier = new ScheduledTaskChangeNotifier();
        var tracker = new TaskActivityTracker(notifier, TimeProvider.System);
        Services.AddSingleton(tracker);

        var runningWithProgress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        notifier.Changed += () =>
        {
            var current = tracker.Current;
            if (current is { IsRunning: true, Message: var m } && m.StartsWith("Enriching actor", StringComparison.Ordinal))
            {
                runningWithProgress.TrySetResult();
            }
            if (current is { IsRunning: false })
            {
                completed.TrySetResult();
            }
        };

        var cut = Render<SettingsMetadata>();
        var minnanoAvTile = cut.FindAll(".status-tile").First(t => t.TextContent.Contains("minnano-av.com"));
        await cut.InvokeAsync(() => minnanoAvTile.Click());

        var enrichButton = cut.FindAll("button").First(b => b.TextContent.Contains("Enrich all actors now"));
        await cut.InvokeAsync(() => enrichButton.Click());

        // The batch runs detached (fire-and-forget, like ImportNow) rather than blocking
        // the drawer for the whole run, so the click resolves quickly with a "queued" message.
        cut.WaitForAssertion(
            () => Assert.Contains("Enrichment queued — see the sidebar for progress.", cut.Markup),
            TimeSpan.FromSeconds(2));
        Assert.DoesNotContain("progress-bar", cut.Markup);
        Assert.DoesNotContain("Cancel", cut.Markup);

        // Live progress surfaces through TaskActivityTracker (the sidebar's "active task"
        // mechanism) instead of inline in the drawer, and deliberately isn't an IScheduledTask
        // run — nothing here touches ScheduledTaskRun/System > Tasks.
        await runningWithProgress.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("minnano-av.com enrichment", tracker.Current!.Name);
        Assert.Equal("Enriching actor 14 of 150 (Mikami Yua)… (9%)", tracker.Current!.Message);

        // Navigating away disposes the page; the batch is application-owned, so it must still
        // finish, report completion and record its outcome.
        await DisposeComponentsAsync();

        tcs.SetResult(new ActorBatchEnrichmentResult(150, 140, 10, 50));

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Enriched 140 of 150 actors (50 fields updated).", tracker.Current!.Message);
        await minnanoAvSettingsService.Received(1).SaveEnrichmentOutcomeAsync(
            Arg.Any<DateTime>(), "Enriched 140 of 150 actors (50 fields updated).", Arg.Any<CancellationToken>());
        // Only the drawer's own "save before enriching" writes the full settings object — the
        // batch's completion never rewrites it.
        await minnanoAvSettingsService.Received(1).SaveAsync(Arg.Any<MinnanoAvSettings>(), Arg.Any<CancellationToken>());
    }

    private static IEnumerable<IStudioDiscoverySource> TwoSources()
    {
        static IStudioDiscoverySource Source(string name)
        {
            var source = Substitute.For<IStudioDiscoverySource>();
            source.SourceName.Returns(name);
            return source;
        }
        return [Source("S1"), Source("Moodyz")];
    }

    [Fact]
    public async Task DiscoveryDrawer_SavesTheCheckedSources_AndTheTileSummaryFollows()
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton(Substitute.For<IR18DevDumpStore>());
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddSingleton<IWarashiSettingsService>(new WarashiSettingsService(factory));
        Services.AddSingleton<IMinnanoAvSettingsService>(new MinnanoAvSettingsService(factory));
        Services.AddSingleton<IR18DevSettingsService>(new R18DevSettingsService(factory));
        Services.AddSingleton<IMediaInfoSettingsService>(new MediaInfoSettingsService(factory));
        Services.AddSingleton<IDiscoverySettingsService>(new DiscoverySettingsService(TwoSources(), factory));
        Services.AddSingleton(NewTaskActivityTracker());
        var cut = Render<SettingsMetadata>();

        cut.FindAll(".status-tile").Single(t => t.TextContent.Contains("Studio sources")).Click();
        cut.Find("#discovery-Moodyz").Change(false);
        cut.Find("#discovery-Moodyz").Closest("form")!.Submit();

        cut.WaitForAssertion(() => Assert.Contains("1 of 2 enabled", cut.Markup));
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.DiscoverySourceSettings.ToDictionaryAsync(r => r.SourceName, r => r.Enabled);
        Assert.Equal(new Dictionary<string, bool> { ["S1"] = true, ["Moodyz"] = false }, rows);
    }
}
