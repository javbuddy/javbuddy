using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

public sealed record CaseScene(int Id, double Start, double? End, int[] Tags, int[] Actors);

public sealed record CaseHighlight(int Id, double Start, double End, int[] Tags, int[] Actors);

public sealed record CaseApex(int Id, double Seconds, int[] Tags, int[] Actors);

/// <summary>One movie's scenes, highlights and apexes with the roll-up (tags up) and inheritance
/// (actors down) they should produce. Ids are 1-based and given in time order, so an
/// item's id is also its position. The pure helpers (ClipRollup, ClipActors) and the SQL queries
/// (ClipRollupQueries) are checked against the same cases, so the two copies of the rules can't drift.
/// Expected maps only list owners whose expected set is non-empty; every other owner expects none.</summary>
public sealed record ClipCase(
    string Name,
    double? Duration,
    int[] Cast,
    CaseScene[] Scenes,
    CaseHighlight[] Highlights,
    CaseApex[] Apexes,
    IReadOnlyDictionary<int, int[]> ExpectedSceneImplicitTags,
    IReadOnlyDictionary<int, int[]> ExpectedHighlightImplicitTags,
    IReadOnlyDictionary<int, int[]> ExpectedSceneActors,
    IReadOnlyDictionary<int, int[]> ExpectedHighlightActors,
    IReadOnlyDictionary<int, int[]> ExpectedApexActors)
{
    public override string ToString() => Name;

    public static string ActorName(int id) => "actor" + id;

    public static string TagName(int id) => "tag" + id;

    public IReadOnlyList<SceneActorItem> CastItems => Cast.Select(Actor).ToList();

    /// <summary>The scenes as the services list them: effective end = explicit end, else the next
    /// scene's start, else the duration.</summary>
    public IReadOnlyList<SceneItem> SceneItems =>
        Scenes.OrderBy(s => s.Start).ThenBy(s => s.Id).Select((s, i) =>
        {
            var next = Scenes.Where(o => o.Start > s.Start || (o.Start == s.Start && o.Id > s.Id)).OrderBy(o => o.Start).ThenBy(o => o.Id).FirstOrDefault();
            var effectiveEnd = s.End ?? next?.Start ?? Duration;
            return new SceneItem(s.Id, i + 1, "Scene " + (i + 1), null, s.Start, s.End, effectiveEnd)
            {
                Tags = s.Tags.Select(Tag).ToList(),
                Actors = s.Actors.Select(Actor).ToList(),
            };
        }).ToList();

    public IReadOnlyList<HighlightItem> HighlightItems =>
        Highlights.OrderBy(h => h.Start).ThenBy(h => h.Id).Select((h, i) =>
            new HighlightItem(h.Id, i + 1, "Highlight " + (i + 1), null, h.Start, h.End, false, 0)
            {
                Tags = h.Tags.Select(Tag).ToList(),
                Actors = h.Actors.Select(Actor).ToList(),
            }).ToList();

    public IReadOnlyList<ApexItem> ApexItems =>
        Apexes.OrderBy(a => a.Seconds).ThenBy(a => a.Id).Select((a, i) =>
            new ApexItem(a.Id, i + 1, a.Seconds, a.Tags.Select(Tag).ToList())
            {
                Actors = a.Actors.Select(Actor).ToList(),
            }).ToList();

    private static SceneTagItem Tag(int id) => new(id, TagName(id), null);

    private static SceneActorItem Actor(int id) => new(id, ActorName(id));
}

public static class ClipCases
{
    private static readonly int[] None = [];

    private static Dictionary<int, int[]> Map(params (int Id, int[] Values)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Values);

    public static IReadOnlyList<ClipCase> List { get; } =
    [
        new("apex-in-highlight-in-scene", 1000, [1, 2, 3],
            [new(1, 0, 100, None, [1, 2])],
            [new(1, 10, 50, None, None)],
            [new(1, 20, [7], None)],
            ExpectedSceneImplicitTags: Map((1, [7])),
            ExpectedHighlightImplicitTags: Map((1, [7])),
            ExpectedSceneActors: Map((1, [1, 2])),
            ExpectedHighlightActors: Map((1, [1, 2])),
            ExpectedApexActors: Map((1, [1, 2]))),

        new("highlight-crosses-boundary", 1000, [1, 2],
            [new(1, 0, 100, None, [1]), new(2, 100, 200, None, [2])],
            [new(1, 90, 110, [5], None)],
            [],
            ExpectedSceneImplicitTags: Map((1, [5]), (2, [5])),
            ExpectedHighlightImplicitTags: Map(),
            ExpectedSceneActors: Map((1, [1]), (2, [2])),
            ExpectedHighlightActors: Map((1, [1])),
            ExpectedApexActors: Map()),

        new("highlight-ends-at-scene-start", 1000, [1],
            [new(1, 100, 200, None, [1])],
            [new(1, 50, 100, [5], None)],
            [],
            ExpectedSceneImplicitTags: Map(),
            ExpectedHighlightImplicitTags: Map(),
            ExpectedSceneActors: Map((1, [1])),
            ExpectedHighlightActors: Map((1, [1])), // starts in no scene → the cast
            ExpectedApexActors: Map()),

        new("highlight-starts-at-scene-end", 1000, [1, 2],
            [new(1, 100, 200, None, [1])],
            [new(1, 200, 250, [5], None)],
            [],
            ExpectedSceneImplicitTags: Map(),
            ExpectedHighlightImplicitTags: Map(),
            ExpectedSceneActors: Map((1, [1])),
            ExpectedHighlightActors: Map((1, [1, 2])),
            ExpectedApexActors: Map()),

        new("apex-on-boundary", 1000, [1, 2],
            [new(1, 0, 100, None, [1]), new(2, 100, 200, None, [2])],
            [],
            [new(1, 100, [9], None)],
            ExpectedSceneImplicitTags: Map((2, [9])),
            ExpectedHighlightImplicitTags: Map(),
            ExpectedSceneActors: Map((1, [1]), (2, [2])),
            ExpectedHighlightActors: Map(),
            ExpectedApexActors: Map((1, [2]))),

        new("apex-in-gap", 1000, [1, 2, 3],
            [new(1, 0, 10, None, [1]), new(2, 90, 100, None, [2])],
            [new(1, 40, 60, None, [3])],
            [new(1, 50, [3], None)],
            ExpectedSceneImplicitTags: Map(),
            ExpectedHighlightImplicitTags: Map((1, [3])),
            ExpectedSceneActors: Map((1, [1]), (2, [2])),
            ExpectedHighlightActors: Map((1, [3])),
            ExpectedApexActors: Map((1, [3]))),

        new("apex-in-gap-no-highlight", 1000, [1, 2],
            [new(1, 0, 10, None, [1])],
            [],
            [new(1, 50, [4], None)],
            ExpectedSceneImplicitTags: Map(),
            ExpectedHighlightImplicitTags: Map(),
            ExpectedSceneActors: Map((1, [1])),
            ExpectedHighlightActors: Map(),
            ExpectedApexActors: Map((1, [1, 2]))),

        new("overlapping-highlights", 1000, [1, 2],
            [],
            [new(1, 0, 50, None, [1]), new(2, 10, 60, None, [2])],
            [new(1, 20, [3], None)],
            ExpectedSceneImplicitTags: Map(),
            ExpectedHighlightImplicitTags: Map((1, [3]), (2, [3])),
            ExpectedSceneActors: Map(),
            ExpectedHighlightActors: Map((1, [1]), (2, [2])),
            ExpectedApexActors: Map((1, [1]))),

        new("explicit-not-implicit", 1000, [1, 2],
            [new(1, 0, 100, [7], [1])],
            [new(1, 0, 10, [7, 8], [1, 2])],
            [new(1, 5, [8], None)],
            ExpectedSceneImplicitTags: Map((1, [8])),
            ExpectedHighlightImplicitTags: Map(),
            ExpectedSceneActors: Map((1, [1])),
            ExpectedHighlightActors: Map((1, [1, 2])),
            ExpectedApexActors: Map((1, [1, 2]))),

        new("not-transitive", 1000, [1],
            [new(1, 0, 100, None, [1]), new(2, 100, 200, None, [1])],
            [new(1, 90, 110, None, None)],
            [new(1, 95, [3], None)],
            ExpectedSceneImplicitTags: Map((1, [3])),
            ExpectedHighlightImplicitTags: Map((1, [3])),
            ExpectedSceneActors: Map((1, [1]), (2, [1])),
            ExpectedHighlightActors: Map((1, [1])),
            ExpectedApexActors: Map((1, [1]))),

        new("open-ended-last-scene", null, [4, 5],
            [new(1, 0, null, None, [4])],
            [],
            [new(1, 9999, [6], None)],
            ExpectedSceneImplicitTags: Map((1, [6])),
            ExpectedHighlightImplicitTags: Map(),
            ExpectedSceneActors: Map((1, [4])),
            ExpectedHighlightActors: Map(),
            ExpectedApexActors: Map((1, [4]))),

        new("open-scene-runs-to-next", 1000, [1, 2],
            [new(1, 0, null, None, [1]), new(2, 100, null, None, [2])],
            [new(1, 50, 150, [5], None)],
            [new(1, 99, [6], None)],
            ExpectedSceneImplicitTags: Map((1, [5, 6]), (2, [5])),
            ExpectedHighlightImplicitTags: Map((1, [6])),
            ExpectedSceneActors: Map((1, [1]), (2, [2])),
            ExpectedHighlightActors: Map((1, [1])),
            ExpectedApexActors: Map((1, [1]))),

        new("scene-without-actors", 1000, [1, 2],
            [new(1, 0, 100, None, None)],
            [new(1, 10, 20, None, None)],
            [],
            ExpectedSceneImplicitTags: Map(),
            ExpectedHighlightImplicitTags: Map(),
            ExpectedSceneActors: Map((1, [1, 2])), // none of its own: the cast
            ExpectedHighlightActors: Map((1, [1, 2])), // and passes it on
            ExpectedApexActors: Map()),

        new("narrowed-apex", 1000, [1, 2, 3],
            [new(1, 0, 100, None, [1, 2, 3])],
            [new(1, 10, 50, None, None)],
            [new(1, 20, None, [2])],
            ExpectedSceneImplicitTags: Map(),
            ExpectedHighlightImplicitTags: Map(),
            ExpectedSceneActors: Map((1, [1, 2, 3])),
            ExpectedHighlightActors: Map((1, [1, 2, 3])),
            ExpectedApexActors: Map((1, [2]))),

        new("apex-follows-crossing-parent-highlight", 1000, [1, 2],
            [new(1, 0, 100, None, [1]), new(2, 100, 200, None, [2])],
            [new(1, 90, 150, None, None)],
            [new(1, 120, None, None)],
            ExpectedSceneImplicitTags: Map(),
            ExpectedHighlightImplicitTags: Map(),
            ExpectedSceneActors: Map((1, [1]), (2, [2])),
            ExpectedHighlightActors: Map((1, [1])),
            ExpectedApexActors: Map((1, [1]))),
    ];

    public static TheoryData<ClipCase> All
    {
        get
        {
            var data = new TheoryData<ClipCase>();
            foreach (var clipCase in List) data.Add(clipCase);
            return data;
        }
    }
}
