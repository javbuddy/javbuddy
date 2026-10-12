using Javbuddy.Services.ActorEnrichment;

namespace Javbuddy.Tests.Services.ActorEnrichment;

public class ActorNameMatcherTests
{
    private static ActorEnrichmentContext Context(string[]? aliases = null, string? r18DevName = null) => new(
        ActorId: 1,
        DisplayName: "Hatano Yui",
        FirstName: "Yui",
        LastName: "Hatano",
        JapaneseNameKanji: "波多野 結衣",
        JapaneseNameKana: "はたの ゆい",
        ExistingAliases: aliases ?? ["Yui H"],
        LinkedMovieCodes: [],
        R18DevName: r18DevName);

    [Fact]
    public void BuildSearchQueries_OrdersKanjiThenFirstLastThenDisplayNameThenAliases()
    {
        var queries = ActorNameMatcher.BuildSearchQueries(Context());

        Assert.Equal(["波多野 結衣", "Yui Hatano", "Hatano Yui", "Yui H"], queries);
    }

    [Fact]
    public void BuildSearchQueries_SkipsDuplicateAliases()
    {
        var queries = ActorNameMatcher.BuildSearchQueries(Context(aliases: ["Hatano Yui", "  Yui H  ", "Yui H"]));

        Assert.Equal(["波多野 結衣", "Yui Hatano", "Hatano Yui", "Yui H"], queries);
    }

    [Theory]
    [InlineData("hatano yui", true)]
    [InlineData("波多野結衣", true)]
    [InlineData("波多野　結衣", true)]
    [InlineData("はたのゆい", true)]
    [InlineData("Yui H", true)]
    [InlineData("Someone Else", false)]
    [InlineData("", false)]
    public void BuildNameMatcher_MatchesKnownNamesIgnoringSpacesInJapanese(string name, bool expected) =>
        Assert.Equal(expected, ActorNameMatcher.BuildNameMatcher(Context())(name));

    [Fact]
    public void BuildNameMatcher_SwappedTwoWordNames_OnlyWhenAllowed()
    {
        var ctx = Context(aliases: ["Smith John"]);

        Assert.False(ActorNameMatcher.BuildNameMatcher(ctx)("John Smith"));
        Assert.True(ActorNameMatcher.BuildNameMatcher(ctx, allowSwappedTwoWordNames: true)("John Smith"));
    }

    [Fact]
    public void BuildNameMatcher_R18DevName_OnlyWhenIncluded()
    {
        var ctx = Context(r18DevName: "Yui Hatano (r18)");

        Assert.False(ActorNameMatcher.BuildNameMatcher(ctx)("Yui Hatano (r18)"));
        Assert.True(ActorNameMatcher.BuildNameMatcher(ctx, includeR18DevName: true)("Yui Hatano (r18)"));
    }

    private sealed record Hit(string Name, bool Exact);

    [Fact]
    public async Task FindMatchAsync_PrefersExactMatchAndStopsAtFirstQueryWithAMatch()
    {
        var searched = new List<string>();
        Task<IReadOnlyList<Hit>> Search(string q, CancellationToken _)
        {
            searched.Add(q);
            IReadOnlyList<Hit> hits = q == "q1"
                ? [new Hit("other", false)]
                : [new Hit("loose", false), new Hit("exact", true)];
            return Task.FromResult(hits);
        }

        var match = await ActorNameMatcher.FindMatchAsync(
            ["q1", "q2", "q3"], Search, h => h.Exact, h => h.Name != "other", CancellationToken.None);

        Assert.Equal("exact", match?.Name);
        Assert.Equal(["q1", "q2"], searched);
    }

    [Fact]
    public async Task FindMatchAsync_NoMatch_ReturnsNull()
    {
        var match = await ActorNameMatcher.FindMatchAsync<Hit>(
            ["q"], (_, _) => Task.FromResult<IReadOnlyList<Hit>>([new Hit("x", true)]), h => h.Exact, _ => false, CancellationToken.None);

        Assert.Null(match);
    }
}
