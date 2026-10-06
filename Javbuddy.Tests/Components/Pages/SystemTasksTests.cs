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

public class SystemTasksTests : BunitContext
{
    private sealed class FakeScheduledTask(string name, string description, TimeSpan interval, bool showInTasksUi = true) : IScheduledTask
    {
        public string Name { get; } = name;
        public string Description { get; } = description;
        public bool ShowInTasksUi { get; } = showInTasksUi;
        public TimeSpan GetInterval() => interval;
        public Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress) => Task.FromResult<string?>("Done");
    }

    private readonly ScheduledTaskCancellationRegistry registry = new();

    private (TestDbContextFactory Factory, ScheduledTaskChangeNotifier Notifier) SetUpServices(params IScheduledTask[] tasks)
    {
        var factory = new TestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);

        var notifier = new ScheduledTaskChangeNotifier();
        Services.AddSingleton(notifier);

        var metrics = new JavbuddyMetrics();
        Services.AddSingleton(metrics);

        Services.AddSingleton(Substitute.For<ILogger<SystemTasks>>());
        Services.AddSingleton(Substitute.For<ILogger<ScheduledTaskRunner>>());
        Services.AddSingleton(registry);
        Services.AddScoped<ScheduledTaskRunner>();
        Services.AddScoped<IScheduledTaskOverviewService, ScheduledTaskOverviewService>();
        Services.AddSingleton(Substitute.For<ILogger<BackgroundJobRunner>>());
        Services.AddSingleton<BackgroundJobRunner>();
        Services.AddSingleton<ScheduledTaskLauncher>();

        if (tasks.Length == 0)
        {
            Services.AddSingleton<IScheduledTask>(new FakeScheduledTask("Image Cache", "Cache images", TimeSpan.FromHours(1)));
        }
        else
        {
            foreach (var task in tasks)
            {
                Services.AddSingleton(task);
            }
        }

        return (factory, notifier);
    }

    [Fact]
    public async Task QueueTable_RunningTask_RendersDismissButton()
    {
        var (factory, _) = SetUpServices();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ScheduledTaskRuns.Add(new ScheduledTaskRun
            {
                TaskName = "Image Cache",
                QueuedAt = DateTime.UtcNow.AddMinutes(-5),
                StartedAt = DateTime.UtcNow.AddMinutes(-4),
                EndedAt = null
            });
            await db.SaveChangesAsync();
        }

        var cut = Render<SystemTasks>();

        var dismissBtn = cut.Find(".tasks-dismiss-btn");
        Assert.NotNull(dismissBtn);
        Assert.Equal("Mark as Failed", dismissBtn.GetAttribute("title"));
        Assert.Contains("tasks-icon-running", cut.Find(".tasks-icon").ClassName);
    }

    [Fact]
    public async Task QueueTable_CompletedTask_DoesNotRenderDismissButton()
    {
        var (factory, _) = SetUpServices();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ScheduledTaskRuns.Add(new ScheduledTaskRun
            {
                TaskName = "Image Cache",
                QueuedAt = DateTime.UtcNow.AddMinutes(-5),
                StartedAt = DateTime.UtcNow.AddMinutes(-4),
                EndedAt = DateTime.UtcNow.AddMinutes(-1),
                Success = true
            });
            await db.SaveChangesAsync();
        }

        var cut = Render<SystemTasks>();

        Assert.Empty(cut.FindAll(".tasks-dismiss-btn"));
        Assert.Contains("tasks-icon-ok", cut.Find(".tasks-icon").ClassName);
    }

    [Fact]
    public async Task QueueTable_ClickingDismiss_InvokesRunnerAndMarksRunFailed()
    {
        var (factory, _) = SetUpServices();
        int runId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var run = new ScheduledTaskRun
            {
                TaskName = "Image Cache",
                QueuedAt = DateTime.UtcNow.AddMinutes(-10),
                StartedAt = DateTime.UtcNow.AddMinutes(-9),
                EndedAt = null
            };
            db.ScheduledTaskRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        var cut = Render<SystemTasks>();

        var dismissBtn = cut.Find(".tasks-dismiss-btn");
        await cut.InvokeAsync(() => dismissBtn.Click());

        await using (var db = await factory.CreateDbContextAsync())
        {
            var run = await db.ScheduledTaskRuns.SingleAsync(r => r.Id == runId);
            Assert.False(run.Success);
            Assert.NotNull(run.EndedAt);
            Assert.Equal("Manually marked as failed", run.ErrorMessage);
        }

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".tasks-dismiss-btn"));
            Assert.Contains("tasks-icon-warning", cut.Find(".tasks-icon").ClassName);
        });
    }

    [Fact]
    public async Task ScheduledTasksChangedElsewhere_RefreshesQueue()
    {
        var (factory, notifier) = SetUpServices();
        var cut = Render<SystemTasks>();

        Assert.Contains("No task runs yet.", cut.Markup);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ScheduledTaskRuns.Add(new ScheduledTaskRun
            {
                TaskName = "Image Cache",
                QueuedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                EndedAt = DateTime.UtcNow,
                Success = true
            });
            await db.SaveChangesAsync();
        }

        await cut.InvokeAsync(notifier.NotifyChanged);

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("No task runs yet.", cut.Markup);
            Assert.Contains("tasks-icon-ok", cut.Find(".tasks-icon").ClassName);
        });
    }

    [Fact]
    public async Task QueueTable_ActiveRun_ClickingCancelSignalsTokenAndShowsCancelling()
    {
        var (factory, _) = SetUpServices();
        int runId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var run = new ScheduledTaskRun
            {
                TaskName = "Image Cache",
                QueuedAt = DateTime.UtcNow.AddMinutes(-2),
                StartedAt = DateTime.UtcNow.AddMinutes(-1),
                EndedAt = null
            };
            db.ScheduledTaskRuns.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }
        using var runCts = new CancellationTokenSource();
        registry.Register(runId, runCts);

        var cut = Render<SystemTasks>();

        var cancelBtn = cut.Find(".tasks-dismiss-btn");
        Assert.Equal("Cancel task", cancelBtn.GetAttribute("title"));
        Assert.Empty(cut.FindAll(".tasks-cancelling"));

        await cut.InvokeAsync(() => cancelBtn.Click());

        Assert.True(runCts.IsCancellationRequested);
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Cancelling…", cut.Find(".tasks-cancelling").TextContent);
            Assert.True(cut.Find(".tasks-dismiss-btn").HasAttribute("disabled"));
        });

        // Cancelling only signals the token — the row is ended by the runner itself, not here.
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null((await db.ScheduledTaskRuns.SingleAsync(r => r.Id == runId)).EndedAt);
        }
    }
}
