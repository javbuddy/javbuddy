namespace Javbuddy.Services.Scenes;

/// <summary>Where a clip's effective actors come from.</summary>
public enum ActorSource { Explicit, Scene, Highlight, Cast }

/// <summary>A scene's, highlight's or apex's effective actors: its own when it has any, else inherited. <see cref="From"/> labels the parent they come from ("scene 2", "highlight 1", "the
/// cast"), null when explicit. They are never empty unless the movie's cast is.</summary>
public sealed record EffectiveActors(IReadOnlyList<SceneActorItem> Actors, ActorSource Source, string? From)
{
    public static EffectiveActors None { get; } = new([], ActorSource.Explicit, null);

    public bool IsInherited => Source != ActorSource.Explicit;
}

public sealed record ClipActorsResult(
    IReadOnlyDictionary<int, EffectiveActors> Scenes,
    IReadOnlyDictionary<int, EffectiveActors> Highlights,
    IReadOnlyDictionary<int, EffectiveActors> Apexes);

/// <summary>Pure inheritance of actors from parent to child: computed from times each time,
/// never stored. A scene has its own actors, else the cast (new scenes start inheriting it, so
/// actors are picked rather than unpicked). A highlight has its own, else those of the scene it starts in, else
/// the cast; an apex its own, else its parent highlight's (<see cref="ParentHighlight"/>), else its enclosing
/// scene's, else the cast — a scene without actors passes the cast on, as inheritance did before
///. Own actors replace inherited ones, so a clip can be narrowed to a subset; an empty own set
/// means "inherit". Tags go the other way (<see cref="ClipRollup"/>). ClipRollupQueries mirrors these rules
/// in SQL for the wall.</summary>
public static class ClipActors
{
    private const string CastLabel = "the cast";

    public static ClipActorsResult Compute(IReadOnlyList<SceneActorItem> cast, IReadOnlyList<SceneItem> scenes, IReadOnlyList<HighlightItem> highlights, IReadOnlyList<ApexItem> apexes)
    {
        var sceneActors = scenes.ToDictionary(s => s.Id, s => ForScene(cast, s.Actors));
        var highlightActors = highlights.ToDictionary(h => h.Id, h => ForHighlight(cast, scenes, h.Actors, h.StartSeconds));
        var apexActors = apexes.ToDictionary(a => a.Id, a => ForApex(cast, scenes, highlights, a.Actors, a.Seconds));
        return new ClipActorsResult(sceneActors, highlightActors, apexActors);
    }

    /// <summary>An apex's parent highlight: the earliest-starting highlight whose [start, end) holds it
    /// (ties by id), else null. ClipTree places the apex under it too.</summary>
    public static HighlightItem? ParentHighlight(IReadOnlyList<HighlightItem> highlights, double seconds) =>
        highlights
            .Where(h => seconds >= h.StartSeconds && seconds < h.EndSeconds)
            .OrderBy(h => h.StartSeconds)
            .ThenBy(h => h.Id)
            .FirstOrDefault();

    /// <summary>What a new highlight starting at <paramref name="startSeconds"/> would inherit, for the add form.</summary>
    public static EffectiveActors ForNewHighlight(IReadOnlyList<SceneActorItem> cast, IReadOnlyList<SceneItem> scenes, double startSeconds) =>
        ForHighlight(cast, scenes, [], startSeconds);

    /// <summary>What a new apex at <paramref name="seconds"/> would inherit, for the add form.</summary>
    public static EffectiveActors ForNewApex(IReadOnlyList<SceneActorItem> cast, IReadOnlyList<SceneItem> scenes, IReadOnlyList<HighlightItem> highlights, double seconds) =>
        ForApex(cast, scenes, highlights, [], seconds);

    /// <summary>What a scene with no actors of its own inherits: the cast.</summary>
    public static EffectiveActors ForNewScene(IReadOnlyList<SceneActorItem> cast) => Cast(cast);

    private static EffectiveActors ForScene(IReadOnlyList<SceneActorItem> cast, IReadOnlyList<SceneActorItem> own) =>
        own.Count > 0 ? Explicit(own) : Cast(cast);

    private static EffectiveActors ForHighlight(IReadOnlyList<SceneActorItem> cast, IReadOnlyList<SceneItem> scenes, IReadOnlyList<SceneActorItem> own, double startSeconds)
    {
        if (own.Count > 0) return Explicit(own);
        return FromScene(cast, EnclosingScene(scenes, startSeconds));
    }

    private static EffectiveActors ForApex(IReadOnlyList<SceneActorItem> cast, IReadOnlyList<SceneItem> scenes, IReadOnlyList<HighlightItem> highlights, IReadOnlyList<SceneActorItem> own, double seconds)
    {
        if (own.Count > 0) return Explicit(own);
        if (ParentHighlight(highlights, seconds) is { } highlight)
        {
            return Inherit(ForHighlight(cast, scenes, highlight.Actors, highlight.StartSeconds), ActorSource.Highlight, $"highlight {highlight.Position}");
        }
        return FromScene(cast, EnclosingScene(scenes, seconds));
    }

    // A scene with actors passes them on; no scene, or one without actors, passes the cast on.
    private static EffectiveActors FromScene(IReadOnlyList<SceneActorItem> cast, SceneItem? scene) =>
        scene is { Actors.Count: > 0 }
            ? new EffectiveActors(Sorted(scene.Actors), ActorSource.Scene, $"scene {scene.Position}")
            : Cast(cast);

    private static SceneItem? EnclosingScene(IReadOnlyList<SceneItem> scenes, double seconds) =>
        scenes.FirstOrDefault(s => ApexRanges.IsWithin(s.StartSeconds, s.EffectiveEndSeconds, seconds));

    // A parent highlight's actors become the apex's inherited ones from that highlight; a highlight that
    // itself fell back to the cast passes the cast on, labelled as such.
    private static EffectiveActors Inherit(EffectiveActors parent, ActorSource source, string from) =>
        parent.Source == ActorSource.Cast ? parent : new EffectiveActors(parent.Actors, source, from);

    private static EffectiveActors Explicit(IReadOnlyList<SceneActorItem> actors) => new(Sorted(actors), ActorSource.Explicit, null);

    private static EffectiveActors Cast(IReadOnlyList<SceneActorItem> cast) => new(Sorted(cast), ActorSource.Cast, CastLabel);

    private static IReadOnlyList<SceneActorItem> Sorted(IReadOnlyList<SceneActorItem> actors) =>
        actors.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.ActorId).ToList();
}
