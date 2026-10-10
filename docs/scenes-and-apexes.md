# Scenes, highlights and apexes

How a movie's scenes, highlights and apexes relate to each other and to the movie, how tags and actors move between them, and where each rule lives in the code. Read this before extending the scene editor (`ClipEditor`), the Scenes wall or Actor Detail's clip sections.

## The four entities

| Entity | Model | Time | Carries | Written to the file |
|---|---|---|---|---|
| Movie | `Movie` | the whole file (`MediaDurationSeconds`) | files, cast (`MovieActor`), tags (`MovieTag`), and its scenes, highlights and apexes | — |
| Scene | `Scene` (`Models/Scenes/Scene.cs`) | range `StartSeconds`–`EndSeconds`; a null end runs to the next scene's start, or the movie's end for the last one | title, actors (`SceneActor`), tags (`SceneTag`), favorite, hidden-from-overview | yes, as chapters |
| Highlight | `MovieHighlight` (`Models/Movies/MovieHighlight.cs`) | range `StartSeconds`–`EndSeconds`, end always set | title, actors (`HighlightActor`), tags (`HighlightTag`), favorite | no |
| Apex | `MovieApex` (`Models/Movies/MovieApex.cs`) | one point, `Seconds` | actors (`ApexActor`), tags (`ApexTag`), favorite | no |

All three belong to the movie directly (`MovieId`). There are no foreign keys between scenes, highlights and apexes: which scene a highlight or apex "is in" is computed from times each time it's needed, never stored. So moving or resizing a scene changes which highlights and apexes count as inside it — and so what rolls up into it and what they inherit — without touching their rows.

Each level stores only its **own** (explicit) tags and actors. Everything else a level shows — tags rolled up from inside it, actors inherited from above it — is computed.

### Scenes partition the movie

Scenes may leave gaps but never overlap (`SceneRanges.Validate`, enforced by `MovieSceneService`), since chapter export needs non-overlapping ranges. `SceneRanges.ResolveEffectiveRanges` orders a movie's scenes by start (id as tiebreak) and fills each null end in, giving the **effective range** every other rule uses. An empty title displays as "Scene N", N being the position by start time.

### Highlights and apexes are independent of scenes

A highlight may overlap other highlights and cross any scene boundary (`HighlightRanges` lays overlapping ones out in timeline lanes). An apex may sit inside a scene or in a gap outside every scene. Neither one is moved, resized or deleted when a scene changes.

### Containment

| Relation | Rule | Code |
|---|---|---|
| Apex in scene | the scene whose effective range `[start, end)` holds it; an open end runs on; an apex exactly on a boundary belongs to the scene starting there | `ApexRanges.IsWithin` |
| Apex in highlight | the highlight's `[start, end)` holds it | `ClipRollup.ApexInHighlight` |
| Apex's **parent** highlight | the earliest-starting highlight holding it (ties by id) | `ClipActors.ParentHighlight` |
| Highlight starts in scene | `IsWithin` on the highlight's start; one starting in a gap after a scene with an explicit end starts in none | `ApexRanges.IsWithin` |
| Highlight overlaps scene | the ranges overlap; touching edges don't count | `ClipRollup.HighlightOverlapsScene` |

## Tags roll up

A scene or highlight shows, as grayed **implicit** tags, the explicit tags of what's inside it; the movie holds the explicit tags of all its clips.

```
Apex tag ──────► every highlight holding it ─┐
     └─────────► the scene holding it        ├─ shown grayed, computed (ClipRollup)
Highlight tag ──► every scene it overlaps ───┘
Scene / highlight / apex tag ──────────────────► the movie (MovieTag.FromClips, stored)
```

- **Not transitive.** A scene's implicit tags come from the explicit tags of the highlights overlapping it and the apexes in it — not from a highlight's own implicit tags. So an apex in a scene-crossing highlight reaches only the scene the apex is in.
- **Exact match.** A subtag rolls up as itself, even when the parent already has its parent tag. Filters still match a parent tag's subtags.
- **Explicit wins.** A tag a level holds explicitly isn't also listed as implicit there; the implicit chip names its sources ("From highlight 2, apex 3").
- **Removing undoes it.** Removing a tag from an apex, or deleting the apex, removes it from everything above — unless that level also holds it explicitly.

### Movie level: stored flags

The movie end of the roll-up is stored, because about ten readers use `MovieTag` (Movies grid and its filter, `MetaGenres`, `.nfo` sync and drift, Actor Detail). A `MovieTag` row has two flags:

- `IsExplicit` — metadata or hand-added. Rows that predate this column are explicit.
- `FromClips` — carried by one of the movie's scenes, highlights or apexes.

A row exists while either flag is set; every reader treats any row as the movie's tag. `ClipTagSync.RefreshAsync` recomputes `FromClips` from the clip tags; `ClipTagSyncService` runs it after every scene-tag, highlight and apex change (and scene delete) and then re-syncs `MetaGenres` and re-checks `.nfo` drift. The services report it as `MovieTagsChanged`, which `ClipEditor` turns into `OnMovieTagsMayHaveChanged` for the host.

- **Removing a movie tag a clip still carries** clears `IsExplicit` only; the tag stays, grayed, in `MovieTagsEditor` and on the Review card (no remove button there).
- **Adding a clip-only tag to the movie** sets `IsExplicit` on the existing row.
- **Metadata never promotes a clip-only row.** `TagNormalization.ApplyToMovieAsync` leaves the flags of rows the metadata lists alone, because clip tags come back as "metadata" twice: from `MetaGenres` on "Apply Rules to Library", and from an `.nfo` that lists them (the `.nfo` includes clip tags). A tag the metadata drops loses `IsExplicit`; the row stays if `FromClips`.
- **Tag merges** move highlight and apex tags with scene tags (`TagService.RepointClipTagsAsync`) and OR the flags together.
- **Self-heal.** `TagNormalization.ApplyToLibraryAsync` refreshes every movie with clip tags, so "Apply Rules to Library" fixes flags a missed write path left behind.

## Actors flow down

```
MovieActor (cast) ──► scene: own actors, else the cast
        │
        └─► highlight: own actors, else those of the scene it starts in, else the cast
                 │
                 └─► apex: own actors, else its parent highlight's, else its scene's, else the cast
```

`ClipActors.Compute` gives each clip its **effective actors** (`EffectiveActors`: the actors, where they come from, and a label like "scene 2"). It is the only definition; the editor, Movie Detail and the cards compute from it directly.

The wall filters on a stored copy: `SceneEffectiveActor`, `HighlightEffectiveActor` and `ApexEffectiveActor` hold every clip's effective actors, written only by `ClipActorSync.RefreshAsync`, which recomputes one whole movie with `ClipActors.Compute` and applies the difference.

- **Stale flag.** `Movie.ClipActorsStale` marks a movie whose rows need recomputing. `ClipActorStaleInterceptor` sets it in the same `SaveChanges` as any tracked change that can alter effective actors (a clip added, deleted or moved; an own-actor or cast link added or removed; `MediaDurationSeconds`; an actor deleted). The three `ExecuteDelete` paths (scene, highlight, apex) call `ClipActorStale.MarkAsync` themselves.
- **Worker.** `ClipActorRefreshWorker` refreshes stale movies on startup and whenever `ClipActorRefreshSignal` fires, one movie per transaction. The wall can lag an edit by a moment; Movie Detail and the editor never do, since they use `ClipActors` directly.
- **First fill and self-heal.** New movies start stale, and the migration made every existing movie stale, so the first start after upgrading fills the tables in the background. "Apply Rules to Library" marks every movie stale again (`TagNormalization.ApplyToLibraryAsync`), fixing rows a missed write path left behind.

- **Scenes inherit the cast.** A new scene, added or imported from chapters, gets no actors of its own (until then it was given every cast member), so its effective actors are the cast, and it passes the cast on to the highlights and apexes inheriting from it. Picking actors narrows it like any other clip; `SetSceneActorsAsync` replaces the set, and an empty set goes back to inheriting.
- **Own actors replace inherited ones**, so a clip can be narrowed to a subset. An empty own set means "inherit": a clip can't be set to "no actors" while the movie has a cast.
- **Inheritance follows changes.** Moving a clip, editing a scene's actors or changing the cast changes what it inherits, without touching any clip's own actors (the stored copy above is refreshed).
- **The A key** opens the Mark apex form with the previous apex's tags and its own actors (`ApexRanges.PreviousFor`); a previous apex with none of its own leaves the new one inheriting. The form also offers the previous apex's tags.
- **In the forms** (`ClipActorChips`, also the scene form), the inherited actors show dimmed and selected, labelled "Inherited from …", and follow the form's time. While inheriting, clicking a chip makes that actor alone the clip's own (picking, not unpicking); **Customize** copies all the inherited ones to remove some from; after that, chips toggle, and **Use inherited** goes back. `AddHighlightAsync` / `AddApexAsync` with no actors store none; `Update…` with an empty list clears to inherited, with `null` leaves them.

### Cast

`MovieActor` is the movie's cast, from metadata / the `.nfo` or the cast editor. It is the only pool scenes, highlights and apexes draw from: `SceneActor`, `HighlightActor` and `ApexActor` all have a `(MovieId, ActorId)` foreign key onto the `MovieActor` link, so an actor outside the cast can't be assigned (`AddSceneActorAsync` and `ClipAssignments.ValidateActorsAsync` also refuse it up front).

- **Leaving the cast cascades.** Removing an actor from a movie's cast by any path (cast editor, the `MetaActresses` re-sync) deletes their `SceneActor`, `HighlightActor` and `ApexActor` rows for that movie — and they drop out of every inherited set.
- **Merging actors keeps them.** `ActorService.MergeAsync` re-points the source actor's `MovieActor`, `SceneActor`, `HighlightActor` and `ApexActor` links onto the target (deduplicating where the target is already there).

## Actor tags

A tag for one actor within a movie or clip ("blonde"), as opposed to a tag for the whole movie or clip. They're ordinary `Tag` rows with `IsActorTag` set, kept out of every plain-tag path: the tag pickers, parent and merge candidates, the plain add paths (the services refuse them), and `TagNormalization` (metadata never resolves onto one). They nest one level like plain tags, within their own kind (a parent matches its subtags in the filters). Only `ActorTagService` adds them, per actor and level: `MovieActorTag`, `SceneActorTag`, `HighlightActorTag` and `ApexActorTag`, each pointing at the movie's `MovieActor` cast link and cascading with it.

- **Flow down, per actor.** `ClipActorTags` is the only definition: an actor's own tags at the nearest level win, in the order actors inherit (scene → movie; highlight → its scene → movie; apex → parent highlight → scene → movie). Own tags replace inherited ones, so a scene can say "brunette" over the movie's "blonde"; removing the last own tag inherits again. An actor with tags of their own on a clip they're no longer effectively on keeps showing.
- **Roll up, per actor.** Like plain tags, only the explicit tags of what's inside count, not transitively: an apex's reach the highlights holding it and its scene, a highlight's the scenes it overlaps.
- **The movie gets plain tags.** `ClipTagSync` counts every actor tag in a movie's `FromClips` set, so `MetaGenres` and the `.nfo` list them (and a movie can carry both "blonde" and "brunette"). Leaving the cast drops an actor's tags by DB cascade, which no service sees, so `ClipActorSync` re-runs `ClipTagSync` for movies that have any; `ActorService.MergeAsync` moves them to the target.
- **Stored for the wall.** `ClipActorSync` also keeps `SceneEffectiveActorTag`, `HighlightEffectiveActorTag` and `ApexEffectiveActorTag` (`IsRolledUp` marks the ones only inside clips carry). `SceneWallFilter.ActorTagIds` filters on them, with `ActorIds` the same actor must have the tag; `MovieGridFilter.ActorTagIds` checks the movie's own rows at every level. The cards show "Mei (Blonde)" from the non-rolled-up rows (`ActorTagLookup`).
- **Editing.** `ActorTagsEditor` (clip level under a scene's, highlight's or apex's edit form) picks tags with `TagSearchAdd` and applies at once, like the scene's actors and tags; Movie Detail uses `ActorTagPills` instead, pills under each cast member, editable under Edit Cast. Hovering an actor on a scene row in the editor, or on a scene or highlight card in "All scenes", opens `ActorTagHover`'s popover: where an inherited actor comes from and their tags (own and inherited, then the clip-derived ones grayed). It is a top-layer popover that `App.razor`'s script shows and places, so a card's overflow or a modal can't cut it off. `ClipTagSync` runs one refresh at a time (its own gate), since the stored-actor worker and an editor can refresh the same movie together. Actor tags are created and nested on Movies > Tags > Actor Tags; a tag can change kind only while nothing uses it and it has no parent or subtags, and actor tags can't be merged.

## The editor

`ClipEditor` shows the three as a tree built by `ClipTree.Build`, each item once:

- a highlight under the scene it **starts** in, with "continues into scene N" when it reaches later scenes;
- an apex under its **parent highlight** (the one it inherits from), else directly under the scene holding it;
- what no scene holds under **Outside scenes**.

A scene's (or Outside scenes') direct highlights and apexes are listed interleaved by time (`ClipTree.InTimeOrder`): a highlight by its start, an apex by its moment.

Scene and highlight rows, and the scene and highlight edit forms, show their implicit tags grayed (`ImplicitChips`); highlight and apex rows show inherited actors grayed, as do Movie Detail's highlight cards. An explicit tag that would roll up anyway (`ClipRollup`'s `RedundantScenes` / `RedundantHighlights`, carried as `RedundantTags` on the tree nodes), an explicit movie tag that is also `FromClips`, and a highlight's or apex's own actors equal to what it would inherit get a `RedundantMark` badge in the scene / highlight / apex forms and Movie tags; removing the tag (or **Use inherited**) is the cleanup, there is no bulk action or migration. Rows and forms are `ClipHighlightRow` / `ClipHighlightForm` and `ClipApexRow` / `ClipApexForm`; `ClipEditor` owns loading (all three lists after every change), the player bridge (`ClipEditor.razor.js`) and the M / Shift+M, H / Shift+H and A shortcuts.

## On the wall and Actor Detail

The Scenes wall and Actor Detail's clip sections use the same rules, in SQL (`ClipRollupQueries`):

| View | Tag filter and options | Actor and actress-attribute filters, options, card actors |
|---|---|---|
| Scenes | own + rolled up from highlights and apexes | own |
| Highlights | own + rolled up from its apexes | effective (own, else inherited) |
| Apexes | own (the **Apex tag** filter) | effective (own, else inherited) |

`ClipRollupQueries` mirrors `ClipRollup` in SQL and reads the stored effective actors for `ClipActors`; `ClipRollupQueriesTests` checks both against the same cases as the pure helpers (`ClipCases`), running `ClipActorSync` first. Each containment rule is one indexed lookup (the latest scene starting at or before a point, the earliest highlight holding an apex) rather than a pairwise `NOT EXISTS`, and the filters use non-correlated `IN (…)`. The actress-attribute options read one appearance per cast link behind the stored rows, since age and cup size depend only on the movie.

## Naming untitled apexes and highlights

`ActorTagLabel.Format` builds `"Aika, Bea — Creampie, Squirt"`: the effective actor names, then the tag names, each list by name. An apex has no title, so its `DisplayLabel` is always this label, or "Apex" when it has neither. An untitled highlight's `DisplayTitle` (`HighlightRanges.DisplayTitle`) is this label, or "Highlight N" (its position by start time) when it has neither; a title always wins.

## Apex counts on scene and highlight cards

A scene or highlight card's diamond badge (`ApexCount`) counts the movie's apexes inside it: `IsWithin` the scene's effective range, or `[start, end)` of the highlight. The wall's **Contains an apex** and **Apex tag** filters use the same containment.

## Playback and navigation

| | Movie player (`VideoPlayerModal`) | Scenes wall, Actor Detail, Movie Detail's Scenes section (`ClipPlayerModal`) |
|---|---|---|
| Scene | seek to its start; drawn as a segment on the scrub bar and timeline | plays `[start, effective end)`; no effective end plays to the end of the file |
| Highlight | seek to its start; drawn on the lane-stacked highlight track (`HighlightSegments`) | plays `[start, end)`, with its own denser trickplay (`HighlightTrickplayService`) |
| Apex | seek to `ApexRanges.StartFor` — its lead-in before it, not before 0; drawn as a diamond (`ApexMarkers`), red when favorite | plays `ApexRanges.ClipFor`: its lead-in before to its tail after, cut at the movie's end |

- **Apex window**: an apex's lead-in and tail (`ApexWindow`, on `ApexItem.Window` and `ApexWallCard.Window`) are its own `MovieApex.LeadInSeconds` / `TailSeconds` where set (the apex form's **Plays** fields, whose **Now** buttons take the playhead's distance before or after the apex, `ApexRanges.OffsetFromPlayhead`), else the defaults from `ApexPlaybackSettingsService` (Settings > UI, or `ApexPlayback__LeadInSeconds` / `ApexPlayback__TailSeconds`; 5 s each by default). `ApexRanges.ValidateWindow` keeps a lead-in in 0–300 s and a tail in 1–300 s. The overrides are relative to the apex, so moving it keeps them. The apex's ±2 s hover preview (`ApexMediaService.HalfSpanSeconds`) doesn't follow the window.

- **Apex countdown**: `ApexRanges.CountdownFor` gives the whole seconds left to the next apex ahead when it's within the configured length (Settings > UI, default 10 s, max 60); `ApexCountdown.razor.js` mirrors it.
- **Jump keys**: `MarkerJumps` gives the H / Shift+H targets (a highlight's start) and A / Shift+A targets (`StartFor` of an apex). In the clip player only markers inside the clip count, and a jump never leaves the clip. While the editor is open, H and A mark highlights and apexes instead.
- **Clip-relative apexes**: `ApexRanges.WithinClip` shifts the apexes inside a clip so the clip player's track starts at 0.

## Deleting things

| Deleted | Effect |
|---|---|
| Scene | its `SceneActor` / `SceneTag` rows and media go; highlights and apexes inside it stay, and count as in another scene or in none (the editor moves them); its tags leave the movie unless something else carries them |
| Highlight | its `HighlightActor` / `HighlightTag` rows and media go; its tags stop rolling up |
| Apex | its `ApexActor` / `ApexTag` rows and preview go; its tags stop rolling up |
| Cast member | their scene, highlight and apex actor links on that movie go (cascade), and they leave every inherited set |
| Tag | its movie and clip links go with it |

## Where the code lives

| Concern | Code | Tests |
|---|---|---|
| Scene CRUD, scene actors and tags | `Services/Scenes/MovieSceneService.cs`, `SceneRanges.cs` | `MovieSceneServiceTests`, `SceneActorsAndTagsTests`, `SceneRangesTests` |
| Highlight CRUD | `Services/Scenes/MovieHighlightService.cs`, `HighlightRanges.cs` | `MovieHighlightServiceTests`, `HighlightRangesTests` |
| Apex CRUD, time helpers | `Services/Scenes/MovieApexService.cs`, `ApexRanges.cs` | `MovieApexServiceTests`, `ApexRangesTests` |
| Tags up (computed) | `Services/Scenes/ClipRollup.cs` | `ClipRollupTests` (over `ClipCases`) |
| Actors down (computed) | `Services/Scenes/ClipActors.cs`; loading per movie in `ClipAssignments.LoadEffectiveActorsAsync` | `ClipActorsTests` (over `ClipCases`) |
| Movie tag flags | `Models/Movies/MovieTag.cs`, `Services/Scenes/ClipTagSync.cs`, `Services/Tags/{TagService,TagNormalization}.cs` | `ClipTagSyncTests`, `MovieTagFlagsTests`, `ClipTagMaintenanceTests` |
| Editor tree | `Services/Scenes/ClipTree.cs`; `Components/Shared/ClipEditor.razor`, `ClipHighlightRow`, `ClipHighlightForm`, `ClipApexRow`, `ClipApexForm`, `ClipActorChips`, `ImplicitChips`, `RedundantMark` | `ClipTreeTests`, `ClipEditor{Scene,Highlight,Apex,Tree}Tests`, `ImplicitChipsTests`; E2E `ClipEditorFlowTests` |
| Wall and Actor Detail queries | `Services/Scenes/SceneWallQueryService.cs`, `ClipRollupQueries.cs` | `SceneWallQueryServiceTests`, `ClipWallFilterTests`, `ClipRollupQueriesTests` |
| Shared actor checks, untitled labels | `Services/Scenes/ClipAssignments.cs`, `ActorTagLabel.cs` | via the service tests |
| Actor merge | `Services/Actors/ActorService.cs` (`MergeAsync`) | `ActorServiceTests`, `SceneActorsAndTagsTests` |
| Jump targets | `Components/Shared/MarkerJumps.cs` | `MarkerJumpsTests` |
