# Jellyfin Integration

What the app actually does with Jellyfin today. Jellyfin's role narrowed significantly at one point: local file-based metadata gathering (`docs/local-library-format.md`) replaced what used to be Jellyfin-sourced metadata. What's documented here is current behavior, verified against the real code (`Services/Jellyfin/JellyfinClient.cs`, `Services/Tasks/JellyfinLinkSyncTask.cs`, `Components/Pages/MovieDetail.razor`).

## What Jellyfin is used for

- **Ownership sync**: matching a movie against the user's selected Jellyfin libraries and, on a match, marking it `Got` and linking `JellyfinItemId`/`JellyfinServerId`/`JellyfinLibraryName`. Matching uses a "first result is the match" rule (`JellyfinClient.LookupInSelectedLibrariesAsync`, applied consistently by `MovieAddService`, `MovieDetail.razor`'s manual refresh, and the background sync task below).
- **Direct-open link**: once linked, `MovieDetail.razor` shows an "Open in Jellyfin" badge/link built from `JellyfinClient.GetWebUrlAsync`.
- **Background sync**: `JellyfinLinkSyncTask` (default interval 6h) checks every movie not yet linked — regardless of how it was added (local scan, javinizer-go, manual) — against the selected libraries and links/marks-Got any match. This is the unattended equivalent of the per-movie "Jellyfin" refresh option on `MovieDetail.razor`.
- **Library selection**: opt-in, on the Jellyfin Connections settings page (`JellyfinConnectionSection.razor`) — **no libraries are selected by default**, so ownership sync does nothing until the user explicitly picks which Jellyfin libraries to match against.

## What Jellyfin is *not* used for anymore

- **Metadata gathering** — resolution, bitrate, codec, and other technical info now come from probing the local video file directly (`Services/MediaInfo/`), not from Jellyfin's API. This was an explicit removal, not an oversight — see `docs/local-library-format.md`.
- **Library import/discovery** — movies are discovered by scanning local library root paths (`Services/LocalLibrary/`), not by querying Jellyfin for its contents. Library Import (`LibraryImport.razor`) was switched from Jellyfin-based discovery to local-file-based scanning.
- **Triggering a rescan on Jellyfin's own side** — `JellyfinClient.RefreshItemAsync` (an item-refresh API call) exists in the client but has no caller anywhere in the app currently; nothing in the UI or background tasks invokes it. The "Jellyfin" option in `MovieDetail`'s refresh dropdown re-runs the *ownership lookup*, not a Jellyfin-side metadata refresh.

## Settings

Connection settings (base URL, API key) and library selection live on Settings > Connections' Jellyfin section, following the same env-var-or-DB pattern as the other integrations (`docs/architecture.md`'s Service layering section).
