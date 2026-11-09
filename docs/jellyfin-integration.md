# Jellyfin Integration

Jellyfin is used, read-only, for one thing: the **"Open in Jellyfin" links** on a movie's page (and in the clip player) and on an actor's page. Everything else — ownership, metadata, playback, trickplay — comes from the local library (`docs/local-library-format.md`). Verified against `Services/Jellyfin/JellyfinClient.cs`, `Services/Tasks/JellyfinLinkSyncTask.cs` and `Components/Pages/MovieDetail.razor`.

## What it does

- **Movie link**: a movie is looked up by its code across the whole Jellyfin server (`JellyfinClient.LookupMovieAsync`, a single `/Items?searchTerm=…` request for Movie items), and the first result is stored as `JellyfinItemId`/`JellyfinServerId`. `MovieDetail.razor` then shows an "Open in Jellyfin" badge built from `JellyfinClient.GetWebUrlAsync`. The lookup runs when a movie is added, on a rescan and in the background (below).
- **A match never changes the movie's status.** Whether a movie is Got comes only from the local library, so a Jellyfin match neither marks a movie Got nor keeps one Got after its files are gone.
- **Actor link**: `JellyfinLinkSyncTask` looks an actor up by name and stores `JellyfinPersonId`, which the actor page uses for its "Open in Jellyfin" badge.
- **Background sync**: `JellyfinLinkSyncTask` (default every 6 h, `Jellyfin__LinkSyncIntervalHours`) looks up every movie and actor that isn't linked yet.

## What it is not used for

- **Ownership / the Got status** — the local library scan decides it.
- **Library selection** — there is none; lookups search the whole server.
- **Metadata** — descriptive and technical info come from the local `.nfo`/video file or javinizer-go.
- **Trickplay** — scrub-bar previews are generated locally (Settings → Metadata → Trickplay); Jellyfin's are no longer used as a fallback.
- **Telling Jellyfin to rescan** — after a torrent is sorted or chapters are written into a file, Jellyfin picks up the change on its own schedule. Javbuddy never writes to Jellyfin.

## Settings

Base URL, external URL, API key and the enable switch live on Settings > Connections > Jellyfin, with the usual env-var-or-DB pattern (`docs/configuration.md`).
