using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Torrents.SortWizard;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Torrents;

public class SortEditorLookupServiceTests
{
    private static SortEditorLookupService Create(TestDbContextFactory factory, ITagService? tags = null) =>
        new(factory, tags ?? Substitute.For<ITagService>());

    [Fact]
    public async Task ToActressDtoAsync_MapsTrackedActorIncludingPipeSeparatedAliases()
    {
        using var factory = new TestDbContextFactory();
        int id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Moe", LastName = "Sakakibara", JapaneseNameKanji = "榊原萌" };
            actor.Aliases.Add(new ActorAlias { Name = "Moe S" });
            actor.Aliases.Add(new ActorAlias { Name = "M. Sakakibara" });
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            id = actor.Id;
        }

        var dto = await Create(factory).ToActressDtoAsync(id);

        Assert.NotNull(dto);
        Assert.Null(dto!.Id);
        Assert.Equal("Moe", dto.FirstName);
        Assert.Equal("Sakakibara", dto.LastName);
        Assert.Equal("榊原萌", dto.JapaneseName);
        Assert.Equal(["Moe S", "M. Sakakibara"], dto.Aliases!.Split('|').Order().Reverse());
    }

    [Fact]
    public async Task ToActressDtoAsync_UnknownActor_ReturnsNull()
    {
        using var factory = new TestDbContextFactory();

        Assert.Null(await Create(factory).ToActressDtoAsync(999));
    }

    [Fact]
    public async Task MatchTrackedActorsAsync_MatchesByKanjiThenName_ParallelToInput()
    {
        using var factory = new TestDbContextFactory();
        int byKanji, byName;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var b = new Actor { FirstName = "Bbb", JapaneseNameKanji = "新木 希空" };
            var c = new Actor { FirstName = "Yui", LastName = "Hatano" };
            db.Actors.AddRange(b, c);
            await db.SaveChangesAsync();
            byKanji = b.Id; byName = c.Id;
        }

        var matches = await Create(factory).MatchTrackedActorsAsync(
        [
            new ActressViewDto { JapaneseName = "新木希空" },
            new ActressViewDto { FirstName = "yui", LastName = "HATANO" },
            new ActressViewDto { FirstName = "Nobody" }
        ]);

        Assert.Equal(3, matches.Count);
        Assert.Equal(byKanji, matches[0]!.ActorId);
        Assert.Equal(byName, matches[1]!.ActorId);
        Assert.Null(matches[2]);
        Assert.False(matches[0]!.HasImage);
    }

    [Fact]
    public async Task GetTopTagsAsync_ReturnsApprovedTagsByUsage_QualifiedWithParent()
    {
        using var factory = new TestDbContextFactory();
        var tags = Substitute.For<ITagService>();
        tags.GetTagsAsync(null, TagSortOrder.UsageDesc, Arg.Any<CancellationToken>()).Returns(
        [
            new TagListItem(1, "VR", 50, false, DateTime.UtcNow),
            new TagListItem(2, "Unreviewed", 40, true, DateTime.UtcNow),
            new TagListItem(3, "Cowgirl", 30, false, DateTime.UtcNow, 9, "Position"),
            new TagListItem(4, "Solo", 20, false, DateTime.UtcNow)
        ]);

        var top = await Create(factory, tags).GetTopTagsAsync(2);

        Assert.Equal(["VR", $"Position{Tag.HierarchyDelimiter}Cowgirl"], top);
    }

    [Fact]
    public async Task MatchTrackedActorsAsync_KanjiStoredWithIdeographicSpace_StillMatches()
    {
        using var factory = new TestDbContextFactory();
        int id;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", JapaneseNameKanji = "三上\u3000悠亜" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            id = actor.Id;
        }

        var matches = await Create(factory).MatchTrackedActorsAsync([new ActressViewDto { JapaneseName = "三上悠亜" }]);

        Assert.Equal(id, Assert.Single(matches)!.ActorId);
    }
}
