using Bunit;
using Javbuddy.Components.Pages;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages;

public class LibraryImportTests : BunitContext
{
    private const string TaskName = "Library Rescan";

    private sealed class GatedRescanTask(Task gate) : IScheduledTask
    {
        public string Name => TaskName;
        public string Description => "";
        public TimeSpan GetInterval() => TimeSpan.FromHours(1);

        public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
        {
            await gate;
            return "done";
        }
    }

    private readonly TestDbContextFactory factory = new();
    private readonly TaskCompletionSource releaseRescan = new();
    private readonly ScheduledTaskRunTracker tracker = new();

    public LibraryImportTests()
    {
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        Services.AddSingleton(new ScheduledTaskChangeNotifier());
        Services.AddSingleton(new JavbuddyMetrics());
        Services.AddSingleton(Substitute.For<ILogger<ScheduledTaskRunner>>());
        Services.AddSingleton(new ScheduledTaskCancellationRegistry());
        Services.AddSingleton(tracker);
        Services.AddScoped<ScheduledTaskRunner>();
        Services.AddScoped<IScheduledTaskOverviewService, ScheduledTaskOverviewService>();
        Services.AddSingleton(Substitute.For<ILogger<BackgroundJobRunner>>());
        Services.AddSingleton<BackgroundJobRunner>();
        Services.AddSingleton<ScheduledTaskLauncher>();
        Services.AddSingleton<IScheduledTask>(new GatedRescanTask(releaseRescan.Task));
    }

    protected override void Dispose(bool disposing)
    {
        releaseRescan.TrySetResult();
        if (disposing) factory.Dispose();
        base.Dispose(disposing);
    }

    private async Task SeedRunAsync(Action<ScheduledTaskRun> configure)
    {
        var run = new ScheduledTaskRun { TaskName = TaskName, QueuedAt = DateTime.UtcNow.AddMinutes(-10) };
        configure(run);
        await using var db = await factory.CreateDbContextAsync();
        db.ScheduledTaskRuns.Add(run);
        await db.SaveChangesAsync();
    }

    [Fact]
    public void NoRunYet_ShowsThePlaceholder_AndAnEnabledStartButton()
    {
        var cut = Render<LibraryImport>();

        Assert.Contains("No rescan has run yet.", cut.Markup);
        var button = cut.Find(".import-btn");
        Assert.Equal("Run Library Rescan", button.TextContent.Trim());
        Assert.False(button.HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(5, "5 movies scanned")]
    [InlineData(1, "1 movie scanned")]
    [InlineData(null, "0 movies scanned")]
    public async Task LastRunSucceeded_ShowsTheScannedMovieCount(int? total, string expected)
    {
        await SeedRunAsync(r =>
        {
            r.StartedAt = DateTime.UtcNow.AddMinutes(-9);
            r.EndedAt = DateTime.UtcNow.AddMinutes(-8);
            r.Success = true;
            r.ProgressTotal = total;
        });

        var cut = Render<LibraryImport>();

        var result = cut.Find(".import-stats-highlight");
        Assert.Equal(expected, result.TextContent.Trim());
        Assert.Empty(cut.FindAll(".import-stats-error"));
    }

    [Fact]
    public async Task LastRunFailed_ShowsTheErrorMessage()
    {
        await SeedRunAsync(r =>
        {
            r.StartedAt = DateTime.UtcNow.AddMinutes(-9);
            r.EndedAt = DateTime.UtcNow.AddMinutes(-8);
            r.Success = false;
            r.ErrorMessage = "root unreachable";
        });

        var cut = Render<LibraryImport>();

        Assert.Equal("root unreachable", cut.Find(".import-stats-error").TextContent.Trim());
    }

    [Fact]
    public async Task RunInProgress_DisablesTheButtonAndShowsItsProgress()
    {
        await SeedRunAsync(r =>
        {
            r.StartedAt = DateTime.UtcNow.AddMinutes(-1);
            r.ProgressStage = "Probing MediaInfo";
            r.ProgressCurrent = 3;
            r.ProgressTotal = 10;
        });

        var cut = Render<LibraryImport>();

        var button = cut.Find(".import-btn");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("Probing MediaInfo… (3/10)", button.TextContent.Trim());
    }

    [Fact]
    public async Task RunInProgressWithoutAStage_ShowsRunning()
    {
        await SeedRunAsync(r => r.StartedAt = DateTime.UtcNow.AddMinutes(-1));

        var cut = Render<LibraryImport>();

        Assert.Equal("Running…", cut.Find(".import-btn").TextContent.Trim());
    }

    [Fact]
    public async Task ClickingStart_QueuesTheRescan_AndDisablesTheButtonWhileItRuns()
    {
        var cut = Render<LibraryImport>();

        await cut.Find(".import-btn").ClickAsync();

        cut.WaitForAssertion(() => Assert.True(cut.Find(".import-btn").HasAttribute("disabled")));
        await using var db = await factory.CreateDbContextAsync();
        var run = await db.ScheduledTaskRuns.SingleAsync();
        Assert.Equal(TaskName, run.TaskName);
        Assert.Null(run.EndedAt);
        Assert.DoesNotContain("already running", cut.Markup);
    }

    [Fact]
    public async Task ClickingStart_WhileTheTaskIsClaimedElsewhere_QueuesNothing()
    {
        // The scheduler (or another tab) has claimed the task but not written its run row yet.
        Assert.True(tracker.TryBegin(TaskName));
        var cut = Render<LibraryImport>();

        await cut.Find(".import-btn").ClickAsync();

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.ScheduledTaskRuns.ToListAsync());
        Assert.False(cut.Find(".import-btn").HasAttribute("disabled"));
    }
}
