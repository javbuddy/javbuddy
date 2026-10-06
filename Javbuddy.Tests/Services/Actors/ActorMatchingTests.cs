using Javbuddy.Services.Actors;

namespace Javbuddy.Tests.Services.Actors;

public class ActorMatchingTests
{
    [Fact]
    public void SplitNames_SplitsOnComma_TrimmingWhitespace()
    {
        var result = ActorMatching.SplitNames("Yui Hatano, Noa Araki ,  Ai Uehara");

        Assert.Equal(["Yui Hatano", "Noa Araki", "Ai Uehara"], result);
    }

    [Fact]
    public void SplitNames_Null_ReturnsEmpty()
    {
        Assert.Empty(ActorMatching.SplitNames(null));
    }

    [Fact]
    public void SplitNames_RemovesEmptyEntries()
    {
        var result = ActorMatching.SplitNames("Yui Hatano,, Noa Araki");

        Assert.Equal(["Yui Hatano", "Noa Araki"], result);
    }

    [Fact]
    public void SplitNames_SplitsOnJapaneseCommaAndSemicolon()
    {
        var result = ActorMatching.SplitNames("波多野結衣、新木希空; Yua Mikami");

        Assert.Equal(["波多野結衣", "新木希空", "Yua Mikami"], result);
    }

    [Theory]
    [InlineData("Yui Hatano", "YH")]
    [InlineData("Madonna", "M")]
    [InlineData(null, "?")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    public void Initials_DerivesFromName(string? name, string expected)
    {
        Assert.Equal(expected, ActorMatching.Initials(name));
    }

    // Overlapping identities on purpose: shared aliases/names across actors (first wins), case
    // differences, internal spaces, a blank kanji, and a display name equal to its reversed form.
    private static readonly List<ActorMatching.ActorLookup> IndexActors =
    [
        new(1, "Hatano Yui", "Yui Hatano", "波多野 結衣", "はたの ゆい", "Yui Hatano R18", ["Shared Alias", "YH"]),
        new(2, "Araki Noa", "Noa Araki", null, null, null, ["shared alias", "Yui Hatano"]),
        new(3, "Madonna", null, "", "まどんな", "hatano yui", null),
        new(4, "Mikami Yua", "Yua Mikami", "三上悠亜", "みかみ ゆあ", null, ["Hatano Yui"]),
        new(5, "Same Name", "Same Name", "同 名", "同名", null, ["同 名", "Same Name"]),
    ];

    public static TheoryData<string> IndexNames() =>
    [
        "Hatano Yui", "hatano yui", "Yui Hatano", "YuiHatano", "Yui Hatano R18", "波多野結衣", "波多野 結衣", "波 多 野 結 衣",
        "はたのゆい", "Shared Alias", "SHAREDALIAS", "shared  alias", "YH", "y h", "Noa Araki", "NoaAraki", "Madonna",
        "ma donna", " ", "まどんな", "三上 悠亜", "みかみゆあ", "Same Name", "SameName", "同名", "Nobody", "Hatano  Yui",
    ];

    [Theory]
    [MemberData(nameof(IndexNames))]
    public void ActorIndex_FindMatch_ReturnsTheFirstActorWhoseIdentitiesMatch(string name)
    {
        var index = new ActorMatching.ActorIndex(IndexActors);

        var expected = IndexActors.FirstOrDefault(actor => actor.Matches(SingleNameSet(name)));

        Assert.Equal(expected?.Id, index.FindMatch(name)?.Id);
    }

    [Theory]
    [MemberData(nameof(IndexNames))]
    public void ActorIndex_AddMatchingIds_AddsEveryActorWhoseIdentitiesMatch(string name)
    {
        var index = new ActorMatching.ActorIndex(IndexActors);
        var actorIds = new HashSet<int> { 99 };

        var matched = index.AddMatchingIds(name, actorIds);

        var expected = IndexActors.Where(actor => actor.Matches(SingleNameSet(name))).Select(actor => actor.Id).Append(99);
        Assert.Equal(expected.Order(), actorIds.Order());
        Assert.Equal(actorIds.Count > 1, matched);
    }

    [Fact]
    public void ActorIndex_FindMatch_PrefersTheEarlierActorWhenSeveralMatch()
    {
        var index = new ActorMatching.ActorIndex(IndexActors);

        // Actor 1's display name is also actor 4's alias; actor 1's reversed name is also actor
        // 2's alias; actor 1 and actor 2 share an alias.
        Assert.Equal(1, index.FindMatch("Hatano Yui")?.Id);
        Assert.Equal(1, index.FindMatch("yui hatano")?.Id);
        Assert.Equal(1, index.FindMatch("Shared Alias")?.Id);
        // Actor 3's R18Dev name matches whole only, so "hatanoyui" falls through to actor 4's alias.
        Assert.Equal(4, index.FindMatch("hatanoyui")?.Id);
    }

    private static HashSet<string> SingleNameSet(string name) => new(StringComparer.OrdinalIgnoreCase) { name };
}
