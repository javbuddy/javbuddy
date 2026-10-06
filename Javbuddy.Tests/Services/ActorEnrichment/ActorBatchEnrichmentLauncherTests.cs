using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.ActorEnrichment;
using Javbuddy.Services.ActorEnrichment.Sources;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.MinnanoAv;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Tasks;
using Javbuddy.Services.Warashi;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Services.ActorEnrichment;

public class ActorBatchEnrichmentLauncherTests
{
    private sealed record Harness(
        ActorBatchEnrichmentLauncher Launcher,
        BackgroundJobRunner Runner,
        TaskActivityTracker Tracker,
        IActorEnrichmentService Enrichment,
        TestDbContextFactory Factory);

    private static Harness CreateHarness()
    {
        var factory = new TestDbContextFactory();
        var enrichment = Substitute.For<IActorEnrichmentService>();

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<AppDbContext>>(factory);
        services.AddScoped<IWarashiSettingsService, WarashiSettingsService>();
        services.AddScoped<IMinnanoAvSettingsService, MinnanoAvSettingsService>();
        services.AddSingleton(enrichment);
        var provider = services.BuildServiceProvider();

        var runner = new BackgroundJobRunner(provider.GetRequiredService<IServiceScopeFactory>(), Substitute.For<ILogger<BackgroundJobRunner>>());
        var tracker = new TaskActivityTracker(new ScheduledTaskChangeNotifier(), TimeProvider.System);
        return new Harness(new ActorBatchEnrichmentLauncher(runner, tracker), runner, tracker, enrichment, factory);
    }

    [Fact]
    public async Task QueueWarashi_SettingsEditedWhileRunning_KeepsTheEditsAndRecordsOnlyTheOutcome()
    {
        var h = CreateHarness();
        using var factory = h.Factory;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.WarashiSettings.Add(new WarashiSettings { Enabled = true, BaseUrl = "https://old.test", RequestDelayMs = 750 });
            await db.SaveChangesAsync();
        }

        var batch = new TaskCompletionSource<ActorBatchEnrichmentResult>();
        h.Enrichment.EnrichAllAsync(Arg.Any<ActorEnrichmentOptions>(), Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(batch.Task);

        var job = h.Launcher.QueueWarashi(overwriteExisting: true);

        // The user changes the provider's settings while the batch is still running.
        await new WarashiSettingsService(factory).SaveAsync(new WarashiSettings
        {
            Enabled = false,
            BaseUrl = "https://new.test",
            RequestDelayMs = 250,
            OverwriteExisting = false
        });

        batch.SetResult(new ActorBatchEnrichmentResult(10, 9, 1, 30));
        await job.WaitAsync(TimeSpan.FromSeconds(5));

        await using var verifyDb = await factory.CreateDbContextAsync();
        var row = await verifyDb.WarashiSettings.SingleAsync();
        Assert.False(row.Enabled);
        Assert.Equal("https://new.test", row.BaseUrl);
        Assert.Equal(250, row.RequestDelayMs);
        Assert.False(row.OverwriteExisting);
        Assert.NotNull(row.LastEnrichedAt);
        Assert.Equal("Enriched 9 of 10 actors (30 fields updated).", row.LastEnrichSummary);

        await h.Enrichment.Received(1).EnrichAllAsync(
            Arg.Is<ActorEnrichmentOptions>(o => o.Source == WarashiActorMetadataSource.SourceNameConstant && o.OverwriteExisting),
            Arg.Any<IProgress<TaskProgress>>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(new TaskActivity("WAPdB enrichment", "Enriched 9 of 10 actors (30 fields updated).", IsRunning: false), h.Tracker.Current);
    }

    [Fact]
    public async Task QueueMinnanoAv_BatchFails_MarksTheActivityFailedAndLeavesTheOutcomeUntouched()
    {
        var h = CreateHarness();
        using var factory = h.Factory;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MinnanoAvSettings.Add(new MinnanoAvSettings { Enabled = true, LastEnrichSummary = "previous run" });
            await db.SaveChangesAsync();
        }

        h.Enrichment.EnrichAllAsync(Arg.Any<ActorEnrichmentOptions>(), Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ActorBatchEnrichmentResult>(new HttpRequestException("site down")));

        await h.Launcher.QueueMinnanoAv(overwriteExisting: false).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new TaskActivity("minnano-av.com enrichment", "Batch enrichment failed: site down", IsRunning: false, Failed: true), h.Tracker.Current);
        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.Equal("previous run", (await verifyDb.MinnanoAvSettings.SingleAsync()).LastEnrichSummary);
    }

    [Fact]
    public async Task QueueWarashi_ApplicationShutsDownMidBatch_CancelsTheBatch()
    {
        var h = CreateHarness();
        using var factory = h.Factory;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Enrichment.EnrichAllAsync(Arg.Any<ActorEnrichmentOptions>(), Arg.Any<IProgress<TaskProgress>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return new ActorBatchEnrichmentResult(0, 0, 0, 0);
            });

        var job = h.Launcher.QueueWarashi(overwriteExisting: false);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await h.Runner.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(job.IsCompleted);
        Assert.Equal((false, true), (h.Tracker.Current!.IsRunning, h.Tracker.Current.Failed));
    }

    [Theory]
    [InlineData(null, null, null, "Enriching…")]
    [InlineData(14, 150, "Mikami Yua", "Enriching actor 14 of 150 (Mikami Yua)… (9%)")]
    [InlineData(3, 4, null, "Enriching actor 3 of 4… (75%)")]
    [InlineData(0, null, "Mikami Yua", "Enriching Mikami Yua…")]
    public void BatchProgressLabel_FormatsProgress(int? current, int? total, string? stage, string expected)
    {
        TaskProgress? progress = current is { } c ? new TaskProgress(c, total, stage) : null;
        Assert.Equal(expected, ActorBatchEnrichmentLauncher.BatchProgressLabel(progress));
    }
}
