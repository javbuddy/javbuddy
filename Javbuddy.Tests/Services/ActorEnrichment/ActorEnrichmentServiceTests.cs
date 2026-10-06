using Javbuddy.Models;
using Javbuddy.Services.ActorEnrichment;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Javbuddy.Tests.Services.ActorEnrichment;

public class ActorEnrichmentServiceTests
{
    [Fact]
    public async Task EnrichActorAsync_UpdatesMissingFields()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami"
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var source = Substitute.For<IActorMetadataSource>();
        source.SourceName.Returns("Local");
        source.Priority.Returns(10);
        source.CanAutoEnrich.Returns(true);
        source.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        source.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new ActorMetadataResult
            {
                SourceName = "Local",
                JapaneseNameKanji = "三上悠亜",
                JapaneseNameKana = "みかみ ゆあ",
                Aliases = ["Yuachan"]
            });

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.GetOrCreateBothAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(("/cache/thumb.webp", "/cache/full.webp"));

        var service = new ActorEnrichmentService([source], factory, imageCacheService);

        var result = await service.EnrichActorAsync(actorId);

        Assert.True(result.Success);
        Assert.Equal("Local", result.SourceName);
        Assert.True(result.FieldsUpdated > 0);
        Assert.True(result.ImageRefreshed);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var enrichedActor = await verifyDb.Actors.Include(a => a.Aliases).SingleAsync(a => a.Id == actorId);
        Assert.Equal("三上悠亜", enrichedActor.JapaneseNameKanji);
        Assert.Equal("みかみ ゆあ", enrichedActor.JapaneseNameKana);
        Assert.Contains(enrichedActor.Aliases, a => a.Name == "Yuachan");
    }

    [Fact]
    public async Task EnrichActorAsync_RespectsOverwriteExistingPolicy()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                JapaneseNameKanji = "Existing Kanji"
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var source = Substitute.For<IActorMetadataSource>();
        source.SourceName.Returns("Local");
        source.Priority.Returns(10);
        source.CanAutoEnrich.Returns(true);
        source.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        source.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new ActorMetadataResult
            {
                SourceName = "Local",
                JapaneseNameKanji = "New Kanji"
            });

        var service = new ActorEnrichmentService([source], factory);

        // When OverwriteExisting = false (default)
        var resultNoOverwrite = await service.EnrichActorAsync(actorId, new ActorEnrichmentOptions { OverwriteExisting = false });
        Assert.True(resultNoOverwrite.Success);

        await using (var verifyDb = await factory.CreateDbContextAsync())
        {
            var updatedActor = await verifyDb.Actors.SingleAsync(a => a.Id == actorId);
            Assert.Equal("Existing Kanji", updatedActor.JapaneseNameKanji);
        }

        // When OverwriteExisting = true
        var resultOverwrite = await service.EnrichActorAsync(actorId, new ActorEnrichmentOptions { OverwriteExisting = true });
        Assert.True(resultOverwrite.Success);

        await using (var verifyDb = await factory.CreateDbContextAsync())
        {
            var updatedActor = await verifyDb.Actors.SingleAsync(a => a.Id == actorId);
            Assert.Equal("New Kanji", updatedActor.JapaneseNameKanji);
        }
    }

    [Fact]
    public async Task EnrichActorAsync_EvaluatesSourcesInPriorityOrder()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Remu", LastName = null };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var sourcePriority20 = Substitute.For<IActorMetadataSource>();
        sourcePriority20.SourceName.Returns("Secondary");
        sourcePriority20.Priority.Returns(20);
        sourcePriority20.CanAutoEnrich.Returns(true);
        sourcePriority20.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        sourcePriority20.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new ActorMetadataResult { SourceName = "Secondary", JapaneseNameKanji = "Secondary Kanji" });

        var sourcePriority10 = Substitute.For<IActorMetadataSource>();
        sourcePriority10.SourceName.Returns("Primary");
        sourcePriority10.Priority.Returns(10);
        sourcePriority10.CanAutoEnrich.Returns(true);
        sourcePriority10.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        sourcePriority10.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new ActorMetadataResult { SourceName = "Primary", JapaneseNameKanji = "Primary Kanji" });

        // Register in reverse order to verify priority sorting
        var service = new ActorEnrichmentService([sourcePriority20, sourcePriority10], factory);

        var result = await service.EnrichActorAsync(actorId);

        Assert.True(result.Success);
        Assert.Equal("Primary", result.SourceName);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var verifiedActor = await verifyDb.Actors.SingleAsync(a => a.Id == actorId);
        Assert.Equal("Primary Kanji", verifiedActor.JapaneseNameKanji);
    }

    [Fact]
    public async Task EnrichActorAsync_ReturnsFail_WhenActorNotFound()
    {
        using var factory = new TestDbContextFactory();
        var service = new ActorEnrichmentService([], factory);

        var result = await service.EnrichActorAsync(99999);
        Assert.False(result.Success);
        Assert.Equal("Actor not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task EnrichActorAsync_WhenSourceNotSpecified_ExcludesSourcesWithCanAutoEnrichFalse()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Remu", LastName = null };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var onDemandSource = Substitute.For<IActorMetadataSource>();
        onDemandSource.SourceName.Returns("OnDemandOnly");
        onDemandSource.Priority.Returns(10);
        onDemandSource.CanAutoEnrich.Returns(false);
        onDemandSource.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);

        var service = new ActorEnrichmentService([onDemandSource], factory);

        var result = await service.EnrichActorAsync(actorId);

        Assert.False(result.Success);
        Assert.Equal("No metadata found from available sources.", result.ErrorMessage);
        await onDemandSource.DidNotReceive().LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichActorAsync_WhenSourceExplicitlySpecified_EvaluatesSourceEvenIfCanAutoEnrichIsFalse()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Remu", LastName = null };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var onDemandSource = Substitute.For<IActorMetadataSource>();
        onDemandSource.SourceName.Returns("OnDemandOnly");
        onDemandSource.Priority.Returns(10);
        onDemandSource.CanAutoEnrich.Returns(false);
        onDemandSource.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        onDemandSource.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new ActorMetadataResult { SourceName = "OnDemandOnly", JapaneseNameKanji = "OnDemand Kanji" });

        var service = new ActorEnrichmentService([onDemandSource], factory);

        var result = await service.EnrichActorAsync(actorId, new ActorEnrichmentOptions { Source = "OnDemandOnly" });

        Assert.True(result.Success);
        Assert.Equal("OnDemandOnly", result.SourceName);
        await onDemandSource.Received(1).LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichActorAsync_SucceedsWithImageRefreshedFalse_WhenImageCacheServiceThrows()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var source = Substitute.For<IActorMetadataSource>();
        source.SourceName.Returns("Local");
        source.Priority.Returns(10);
        source.CanAutoEnrich.Returns(true);
        source.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        source.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new ActorMetadataResult { SourceName = "Local", JapaneseNameKanji = "三上悠亜" });

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.GetOrCreateBothAsync(actorId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<(string?, string?)>(
                new ArgumentException("Actor image must be JPEG, PNG, or WebP. (Parameter 'bytes')")));

        var logger = Substitute.For<ILogger<ActorEnrichmentService>>();
        var service = new ActorEnrichmentService([source], factory, imageCacheService, logger);

        var result = await service.EnrichActorAsync(actorId);

        Assert.True(result.Success);
        Assert.False(result.ImageRefreshed);
        Assert.True(result.FieldsUpdated > 0);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task EnrichAllAsync_ContinuesProcessingRemainingActors_WhenOneActorThrows()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.AddRange(
                new Actor { FirstName = "First", LastName = "Actor" },
                new Actor { FirstName = "Second", LastName = "Actor" });
            await db.SaveChangesAsync();
        }

        // IsAvailableAsync is not wrapped in a per-source try/catch inside EnrichActorAsync, so it
        // throws an unhandled exception for every actor processed - the scenario EnrichAllAsync's
        // per-actor try/catch needs to isolate so the batch keeps going instead of aborting outright.
        var source = Substitute.For<IActorMetadataSource>();
        source.SourceName.Returns("Local");
        source.Priority.Returns(10);
        source.CanAutoEnrich.Returns(true);
        source.IsAvailableAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("Unexpected failure")));

        var logger = Substitute.For<ILogger<ActorEnrichmentService>>();
        var service = new ActorEnrichmentService([source], factory, null, logger);

        var result = await service.EnrichAllAsync();

        Assert.Equal(2, result.TotalProcessed);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(2, result.Failed);
    }

    [Fact]
    public async Task EnrichAllAsync_SharesOneRunCacheAcrossActors_WhileSingleActorRunsGetNone()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.AddRange(actor, new Actor { FirstName = "Remu", LastName = "Suzumori" });
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var contexts = new List<ActorEnrichmentContext>();
        var source = Substitute.For<IActorMetadataSource>();
        source.SourceName.Returns("Local");
        source.Priority.Returns(10);
        source.CanAutoEnrich.Returns(true);
        source.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        source.LookupAsync(Arg.Do<ActorEnrichmentContext>(contexts.Add), Arg.Any<CancellationToken>())
            .Returns((ActorMetadataResult?)null);

        var service = new ActorEnrichmentService([source], factory);

        await service.EnrichAllAsync();
        await service.EnrichAllAsync();
        await service.EnrichActorAsync(actorId);

        Assert.Equal(5, contexts.Count);
        Assert.NotNull(contexts[0].RunCache);
        Assert.Same(contexts[0].RunCache, contexts[1].RunCache);
        Assert.NotNull(contexts[2].RunCache);
        Assert.NotSame(contexts[0].RunCache, contexts[2].RunCache);
        Assert.Null(contexts[4].RunCache);
    }

    [Fact]
    public async Task EnrichAllAsync_ReportsProgress_WithActorNameAndCurrentTotal()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.AddRange(
                new Actor { FirstName = "Yua", LastName = "Mikami" },
                new Actor { FirstName = "Remu", LastName = "Suzumori" });
            await db.SaveChangesAsync();
        }

        var source = Substitute.For<IActorMetadataSource>();
        source.SourceName.Returns("Local");
        source.Priority.Returns(10);
        source.CanAutoEnrich.Returns(true);
        source.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        source.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new ActorMetadataResult { SourceName = "Local", JapaneseNameKanji = "Kanji" });

        var service = new ActorEnrichmentService([source], factory);

        var reports = new List<TaskProgress>();
        var progress = new ImmediateProgress<TaskProgress>(reports.Add);

        await service.EnrichAllAsync(progress: progress);

        Assert.Equal(2, reports.Count);
        Assert.Equal(new TaskProgress(1, 2, "Mikami Yua"), reports[0]);
        Assert.Equal(new TaskProgress(2, 2, "Suzumori Remu"), reports[1]);
    }

    private static async Task<int> SeedActorAsync(TestDbContextFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
        db.Actors.Add(actor);
        await db.SaveChangesAsync();
        return actor.Id;
    }

    private static IActorMetadataSource Source(string name, int priority)
    {
        var source = Substitute.For<IActorMetadataSource>();
        source.SourceName.Returns(name);
        source.Priority.Returns(priority);
        source.CanAutoEnrich.Returns(true);
        source.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        return source;
    }

    [Fact]
    public async Task EnrichActorAsync_CallerCancelledAtFinalSource_PropagatesInsteadOfNoMetadata()
    {
        using var factory = new TestDbContextFactory();
        var actorId = await SeedActorAsync(factory);
        using var cts = new CancellationTokenSource();
        var first = Source("First", 10);
        first.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>()).Returns((ActorMetadataResult?)null);
        var last = Source("Last", 20);
        last.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ActorMetadataResult?>>(call =>
            {
                cts.Cancel();
                throw new OperationCanceledException(call.Arg<CancellationToken>());
            });
        var service = new ActorEnrichmentService([first, last], factory, null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EnrichActorAsync(actorId, ct: cts.Token));
    }

    [Fact]
    public async Task EnrichActorAsync_CallerCancelledAtFirstSource_DoesNotTryLaterSources()
    {
        using var factory = new TestDbContextFactory();
        var actorId = await SeedActorAsync(factory);
        using var cts = new CancellationTokenSource();
        var first = Source("First", 10);
        first.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ActorMetadataResult?>>(call =>
            {
                cts.Cancel();
                throw new OperationCanceledException(call.Arg<CancellationToken>());
            });
        var second = Source("Second", 20);
        var service = new ActorEnrichmentService([first, second], factory, null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EnrichActorAsync(actorId, ct: cts.Token));
        await second.DidNotReceiveWithAnyArgs().LookupAsync(default!, default);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("timeout")]
    public async Task EnrichActorAsync_ProviderFailureWithoutCallerCancellation_FallsBackToNextSource(string failure)
    {
        using var factory = new TestDbContextFactory();
        var actorId = await SeedActorAsync(factory);
        var failing = Source("Failing", 10);
        Exception error = failure == "http" ? new HttpRequestException("connection refused") : new TaskCanceledException("provider timeout");
        failing.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ActorMetadataResult?>>(_ => throw error);
        var fallback = Source("Fallback", 20);
        fallback.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new ActorMetadataResult { SourceName = "Fallback", JapaneseNameKanji = "三上悠亜" });
        var service = new ActorEnrichmentService([failing, fallback], factory, null);

        var result = await service.EnrichActorAsync(actorId);

        Assert.True(result.Success);
        Assert.Equal("Fallback", result.SourceName);
    }

    [Fact]
    public async Task EnrichActorAsync_SkipsNonStandardCupSize()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var source = Substitute.For<IActorMetadataSource>();
        source.SourceName.Returns("Local");
        source.Priority.Returns(10);
        source.CanAutoEnrich.Returns(true);
        source.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        source.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new ActorMetadataResult { SourceName = "Local", HeightCm = 159, CupSize = "L" });

        var service = new ActorEnrichmentService([source], factory, Substitute.For<IActorImageCacheService>());

        var result = await service.EnrichActorAsync(actorId);

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var enriched = await verifyDb.Actors.SingleAsync(a => a.Id == actorId);
        Assert.Null(enriched.CupSize);
        Assert.Equal(159, enriched.HeightCm);
    }

    [Fact]
    public async Task EnrichActorAsync_SkipsBirthDateMakingActorUnder18()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami", BirthDate = new DateTime(1993, 8, 16) };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var source = Substitute.For<IActorMetadataSource>();
        source.SourceName.Returns("Local");
        source.Priority.Returns(10);
        source.CanAutoEnrich.Returns(true);
        source.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        source.LookupAsync(Arg.Any<ActorEnrichmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new ActorMetadataResult
            {
                SourceName = "Local",
                HeightCm = 159,
                BirthDate = DateTime.UtcNow.Date.AddYears(-17)
            });

        var service = new ActorEnrichmentService([source], factory);

        var result = await service.EnrichActorAsync(actorId, new ActorEnrichmentOptions { OverwriteExisting = true });

        Assert.True(result.Success);
        await using var verifyDb = await factory.CreateDbContextAsync();
        var enriched = await verifyDb.Actors.SingleAsync(a => a.Id == actorId);
        Assert.Equal(new DateTime(1993, 8, 16), enriched.BirthDate);
        Assert.Equal(159, enriched.HeightCm);
    }
}
