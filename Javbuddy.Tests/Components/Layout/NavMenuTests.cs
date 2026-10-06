using Bunit;
using Javbuddy.Components.Layout;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Tasks;
using Javbuddy.Services.Torrents;
using Javbuddy.Tests.TestSupport;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Components.Layout;

/// <summary>Coverage for the sidebar's Activity badge — the number of torrents still sitting in
/// the queue (same RemovedFromClientAt/SortedAt filter as ActivityQueue.razor's own row query),
/// kept live via <see cref="TorrentChangeNotifier"/> the same way Movies.razor and
/// ActivityQueue.razor stay live — and for the active-task status line, kept live via
/// <see cref="ScheduledTaskChangeNotifier"/> the same way.</summary>
public class NavMenuTests : BunitContext
{
    private sealed class FakeTask(string name, bool showInTasksUi = true) : IScheduledTask
    {
        public string Name { get; } = name;
        public string Description => "";
        public bool ShowInTasksUi => showInTasksUi;
        public TimeSpan GetInterval() => TimeSpan.FromMinutes(5);
        public Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress) => Task.FromResult<string?>(null);
    }

    private TestDbContextFactory SetUpServices(TorrentChangeNotifier? notifier = null, ScheduledTaskChangeNotifier? taskNotifier = null, params IScheduledTask[] tasks)
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton(notifier ?? new TorrentChangeNotifier());
        var scheduledTaskNotifier = taskNotifier ?? new ScheduledTaskChangeNotifier();
        Services.AddSingleton(scheduledTaskNotifier);
        Services.AddSingleton(new TaskActivityTracker(scheduledTaskNotifier, TimeProvider.System));
        Services.AddSingleton(Substitute.For<ILogger<NavMenu>>());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.QBittorrent.IQBittorrentClient>());
        Services.AddScoped<ITorrentQueueService, TorrentQueueService>();
        Services.AddScoped<IScheduledTaskOverviewService, ScheduledTaskOverviewService>();
        foreach (var task in tasks)
        {
            Services.AddSingleton(task);
        }
        return factory;
    }

    [Fact]
    public void NoActiveTorrents_HidesTheBadge()
    {
        using var factory = SetUpServices();

        var cut = Render<NavMenu>();

        Assert.Empty(cut.FindAll(".nav-badge"));
    }

    [Fact]
    public async Task QueuedDownloadingSeedingCompletedAndErrorTorrents_AllShowInTheBadge()
    {
        using var factory = SetUpServices();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movies = Enumerable.Range(1, 5).Select(i => new Movie { Code = $"AAA-{i:000}" }).ToArray();
            db.Movies.AddRange(movies);
            await db.SaveChangesAsync();

            db.TorrentDownloads.AddRange(
                new TorrentDownload { MovieId = movies[0].Id, MovieCode = "AAA-001", Status = TorrentDownloadStatus.Queued },
                new TorrentDownload { MovieId = movies[1].Id, MovieCode = "AAA-002", Status = TorrentDownloadStatus.Downloading },
                new TorrentDownload { MovieId = movies[2].Id, MovieCode = "AAA-003", Status = TorrentDownloadStatus.Seeding },
                new TorrentDownload { MovieId = movies[3].Id, MovieCode = "AAA-004", Status = TorrentDownloadStatus.Completed },
                new TorrentDownload { MovieId = movies[4].Id, MovieCode = "AAA-005", Status = TorrentDownloadStatus.Error });
            await db.SaveChangesAsync();
        }

        var cut = Render<NavMenu>();

        Assert.Equal("5", cut.Find(".nav-badge").TextContent);
    }

    [Fact]
    public async Task RemovedOrSortedTorrents_DoNotShowInTheBadge()
    {
        using var factory = SetUpServices();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movies = new[] { new Movie { Code = "AAA-001" }, new Movie { Code = "AAA-002" } };
            db.Movies.AddRange(movies);
            await db.SaveChangesAsync();

            db.TorrentDownloads.AddRange(
                new TorrentDownload { MovieId = movies[0].Id, MovieCode = "AAA-001", Status = TorrentDownloadStatus.Removed, RemovedFromClientAt = DateTime.UtcNow },
                new TorrentDownload { MovieId = movies[1].Id, MovieCode = "AAA-002", Status = TorrentDownloadStatus.Completed, SortedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var cut = Render<NavMenu>();

        Assert.Empty(cut.FindAll(".nav-badge"));
    }

    [Fact]
    public async Task TorrentChangeNotification_RefreshesTheBadgeCount()
    {
        var notifier = new TorrentChangeNotifier();
        using var factory = SetUpServices(notifier);

        var cut = Render<NavMenu>();
        Assert.Empty(cut.FindAll(".nav-badge"));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "AAA-001" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            db.TorrentDownloads.Add(new TorrentDownload { MovieId = movie.Id, MovieCode = "AAA-001", Status = TorrentDownloadStatus.Downloading });
            await db.SaveChangesAsync();
        }

        await cut.InvokeAsync(notifier.NotifyChanged);
        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find(".nav-badge").TextContent));
    }

    [Fact]
    public void NoRunningTask_HidesTheActiveTaskStatus()
    {
        using var factory = SetUpServices(tasks: new FakeTask("Jellyfin Link Sync"));

        var cut = Render<NavMenu>();

        Assert.Empty(cut.FindAll(".active-task-status"));
    }

    [Fact]
    public async Task RunningTask_ShowsNameStageAndProgressInSeparateActiveTaskStatusElements()
    {
        using var factory = SetUpServices(tasks: new FakeTask("Jellyfin Link Sync"));
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ScheduledTaskRuns.Add(new ScheduledTaskRun
            {
                TaskName = "Jellyfin Link Sync",
                QueuedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                ProgressCurrent = 3,
                ProgressTotal = 10,
                ProgressStage = "Linking movies",
            });
            await db.SaveChangesAsync();
        }

        var cut = Render<NavMenu>();

        Assert.Equal("Jellyfin Link Sync", cut.Find(".active-task-name").TextContent);
        Assert.Equal("Linking movies", cut.Find(".active-task-stage").TextContent);
        Assert.Equal("(3/10)", cut.Find(".active-task-progress").TextContent);
    }

    [Fact]
    public async Task RunningTaskHiddenFromTasksUi_IsExcludedFromTheActiveTaskStatus()
    {
        using var factory = SetUpServices(tasks: new FakeTask("QBittorrent Sync", showInTasksUi: false));
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ScheduledTaskRuns.Add(new ScheduledTaskRun { TaskName = "QBittorrent Sync", QueuedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var cut = Render<NavMenu>();

        Assert.Empty(cut.FindAll(".active-task-status"));
    }

    [Fact]
    public async Task ScheduledTaskChangeNotification_RefreshesTheActiveTaskStatus()
    {
        var taskNotifier = new ScheduledTaskChangeNotifier();
        using var factory = SetUpServices(taskNotifier: taskNotifier, tasks: new FakeTask("Jellyfin Link Sync"));

        var cut = Render<NavMenu>();
        Assert.Empty(cut.FindAll(".active-task-status"));

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ScheduledTaskRuns.Add(new ScheduledTaskRun { TaskName = "Jellyfin Link Sync", QueuedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await cut.InvokeAsync(taskNotifier.NotifyChanged);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".active-task-status")));
    }

    [Fact]
    public async Task ManualScan_ShowsProgressThenFailureInTheSameSidebarIndicator()
    {
        using var factory = SetUpServices();
        var tracker = Services.GetRequiredService<TaskActivityTracker>();
        var cut = Render<NavMenu>();

        long activityId = 0;
        await cut.InvokeAsync(() => activityId = tracker.Start("Jellyfin · ABC-123", "Checking Jellyfin…"));
        cut.WaitForAssertion(() => Assert.Contains("Checking Jellyfin…", cut.Find(".active-task-status").TextContent));
        Assert.Single(cut.FindAll(".active-task-spinner"));

        await cut.InvokeAsync(() => tracker.Complete(activityId, "Jellyfin is not configured.", failed: true));
        cut.WaitForAssertion(() => Assert.Contains("Jellyfin is not configured.", cut.Find(".active-task-status").TextContent));
        Assert.Empty(cut.FindAll(".active-task-spinner"));
        Assert.Single(cut.FindAll(".active-task-result-error"));
    }

    [Fact]
    public void NavigatingToActorsSection_ExpandsItsSubItems()
    {
        // NavMenu is now @rendermode InteractiveServer (needed for the live badge above), which
        // means it persists as one long-lived component instance across navigations instead of
        // being freshly re-rendered per request like before. Nothing re-renders it on navigation
        // unless it explicitly subscribes to NavigationManager.LocationChanged — without that,
        // clicking "Actors" changes the URL but the sidebar never shows Actors' sub-items.
        using var factory = SetUpServices();

        var cut = Render<NavMenu>();
        Assert.DoesNotContain("actors/add/new", cut.Markup);

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("actors");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("actors/photos", cut.Markup);
            Assert.Contains("actors/add/new", cut.Markup);
        });
    }

    [Fact]
    public void Always_RendersTheSidebarCollapseToggle()
    {
        // The toggle's collapsed/expanded visuals are pure CSS driven by a data-sidebar-collapsed
        // attribute a client-side script sets on <html> (mirroring the ls-theme cookie
        // pattern) — nothing bUnit can observe, so this only guards the toggle button itself existing.
        using var factory = SetUpServices();

        var cut = Render<NavMenu>();

        var toggle = cut.Find(".sidebar-collapse-toggle");
        Assert.Contains("lsSidebar.toggle()", toggle.GetAttribute("onclick"));
    }

    [Fact]
    public async Task MultipleVideoRepairsQueued_SidebarActiveTaskIndicatorUpdatesToEachRunningJob()
    {
        var taskNotifier = new ScheduledTaskChangeNotifier();
        using var factory = SetUpServices(taskNotifier: taskNotifier);
        var tracker = Services.GetRequiredService<TaskActivityTracker>();
        var jobTracker = new Javbuddy.Services.VideoRepair.VideoRepairJobTracker(tracker);
        var cut = Render<NavMenu>();

        var firstGate = new TaskCompletionSource();
        var secondGate = new TaskCompletionSource();

        var first = jobTracker.GetOrStart(1, null, "AAA-001", "AAA-001.mp4", async (_, _) =>
        {
            await firstGate.Task;
            return Javbuddy.Services.VideoRepair.VideoRepairResult.Ok("/path/AAA-001.mp4");
        });

        var second = jobTracker.GetOrStart(2, null, "BBB-002", "BBB-002.mp4", async (_, _) =>
        {
            await secondGate.Task;
            return Javbuddy.Services.VideoRepair.VideoRepairResult.Ok("/path/BBB-002.mp4");
        });

        // First repair is running; sidebar must show the first movie.
        cut.WaitForAssertion(() => Assert.Equal("Repairing · AAA-001", cut.Find(".active-task-name").TextContent));

        // Complete the first repair; second repair begins running.
        firstGate.SetResult();
        await first.Task!;

        // Sidebar must update to reflect the second movie now actively repairing.
        cut.WaitForAssertion(() => Assert.Equal("Repairing · BBB-002", cut.Find(".active-task-name").TextContent));

        // Complete the second repair.
        secondGate.SetResult();
        await second.Task!;

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Repairing · BBB-002", cut.Find(".active-task-name").TextContent);
            Assert.Contains("Repaired BBB-002.mp4 (BBB-002).", cut.Find(".active-task-message").TextContent);
            Assert.Single(cut.FindAll(".active-task-result"));
        });
    }
}
