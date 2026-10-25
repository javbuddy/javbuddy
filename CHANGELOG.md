# Changelog

All notable changes to this project are documented in this file, in [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format. This project adheres to [Semantic Versioning](https://semver.org/) (pre-1.0: breaking changes may land in a minor bump).

## [Unreleased]

### Changed

- DeoVR's list thumbnails are now each movie's fanart downscaled to an 800px JPEG and kept in the image cache, instead of the full-size original or the remote cover, so DeoVR's lists load faster. A WebP fanart now gets a thumbnail too. While DeoVR is enabled, the Image Cache task builds them ahead of time; any not built yet are built on first request.

## [0.3.6] - 2026-10-22

### Changed

- The video player's header and scrub bar (or clip controls) now overlay the video instead of taking space above and below it, so the video fills the whole player and no longer resizes when the controls hide.

## [0.3.5] - 2026-10-20

### Fixed

- The video player's hidden controls stay hidden when the next clip starts (e.g. while playing scenes), in and out of fullscreen, instead of reappearing.
- Scrubbing the Scenes timeline on Movie Detail shows the "Generating previews…" placeholder with progress while the movie's trickplay is being generated, as the player does, and switches to thumbnails once it's ready.

## [0.3.4] - 2026-10-19

### Changed

- The Movies, Actor Detail and DeoVR filters replace "Has scenes" with a Scenes range, and add Highlights and Apexes ranges. Each is a slider from 0 to the most any movie has; dragging the top thumb to the end means "or more". A saved "Has scenes" becomes Scenes 1 or more.

## [0.3.3] - 2026-10-19

### Added

- Actors have a "Transgender" checkbox on Edit Actor, shown as a badge on Actor Detail. The Actors list can filter to transgender actors or exclude them, and the Actress Attributes filter on Movies, Scenes & Highlights and the DeoVR lists gains a Transgender group (Only / Exclude) once any such actor is in view.

### Changed

- Actor tag pills on Movie Detail show a subtag with its parent ("Hair › Long"), and the "+ tag" search stays open after adding one so several tags can be added in a row.

## [0.3.2] - 2026-10-17

### Added

- Poster Options has a "Show Versions Badge" option (on by default). A movie with several file versions shows "N versions" as a badge in the poster's bottom-right corner, instead of in the text under the poster.

### Fixed

- Scrolling the Movies page no longer blanks the grid, jumps the scrollbar or shifts rows when some posters had an extra or wrapped line under them, e.g. "N versions" with the Size line hidden. Every poster card is the same height again: the version count moved onto the poster as a badge, and size, quality and status lines are cut off with "…" instead of wrapping.

## [0.3.1] - 2026-10-17

### Changed

- ffmpeg work now runs in two lanes of one job each. Scene stills, hover previews and highlight-clip trickplay no longer wait behind a long job such as full-movie trickplay, scene detection, a chapter remux, a repair or a VR merge. Long jobs now run one at a time on every machine, where before two could run on machines with four or more cores.

## [0.3.0] - 2026-10-17

### Added

- Multi-select filters have a "Match all" checkbox beside Select All and None (shown once two or more values are selected): a row must have every selected value instead of any. It is on the Movies page's Genre and Actor tag filters and the Scenes page's Tag, Actor tag and Apex tag filters; for actor tags, one actor must have them all. Off by default, saved with the other filters, and turned off again when fewer than two values are left selected (#68).
- The r18.dev catalog's Genre filter shows its "Match all" checkbox in the same place, beside Select All and None.

### Changed

- Browsing r18.dev releases, the status counts and the Add New suggestions reuse one snapshot of your library for up to 10 seconds instead of reloading every movie on each call (each keystroke in the wizard). Adding, removing or changing movies refreshes it immediately (#57).
- Images fetched from a metadata provider's URL (movie covers and backdrops, actor portraits) are now downloaded with a 30 s timeout, a 20 MB cap and an image content-type check, like the "from URL" imports. Javinizer posters, Warashi images and `.torrent` files (10 MB) are size-capped too, so a wrong or hostile URL can no longer fill the server's memory (#58).
- Only one or two ffmpeg jobs run at a time (two on machines with four or more cores), however many of the scene-media, trickplay, repair, VR-merge and scene-detection queues have work. Before, they all decoded video together and could saturate the CPU and memory. ffmpeg now logs errors only, and cancelling a probe stops its ffprobe process (#59).
- The Tags page search, the Missing page text filter and the add-tag and add-actor search boxes wait for a short pause in typing (250 ms) before searching, instead of querying and re-rendering on every keystroke (#60).
- An actor's page filters and sorts their movies once per change instead of several times on every render, so typing in the filter box or toggling a filter is quicker on large filmographies (#62).
- The database is now always kept in SQLite WAL mode, so pages keep loading while a background task writes instead of waiting for its commit. Databases Javbuddy created were already in WAL mode (EF Core sets it when it creates the file), so the `-wal` and `-shm` files next to the database are not new; the app now also switches a database created some other way, and logs a warning and keeps the previous journal mode on a filesystem that can't support WAL. Connections now use `synchronous=NORMAL` in WAL mode, a lock wait timeout and a larger page cache, and the `-wal` file is trimmed back to 64 MB after a checkpoint instead of keeping its largest size (#55).
- The Movies page's status counts and "Added" sort, the Jellyfin library filter, the NFO-drift counts and filter, the stale clip-actor refresh and the scheduler's last-run lookup use new indexes instead of scanning the whole table (#55).
- Finished scheduled-task runs older than 90 days are deleted daily; each task's latest run is always kept (#55).
- The image cache size on the metrics endpoint is only recalculated while something is scraping `/metrics` (the first pass runs at startup, and the first scrape after a quiet spell starts a refresh straight away), and the cache directory is walked once instead of once per file type. The System page's own stats are unchanged. Actor images and actor photos regenerated from their stored originals now count toward the image cache size limit, which previously relied on that per-minute walk to notice them (#56).
- The System page's uptime clock now ticks in the browser, so an open System page no longer re-renders on the server or sends an update every second (#61).
- Hover previews on the Scenes wall start sooner: the pointer only has to rest on a card for 75 ms (was 150 ms), and the preview video begins loading right away.
- Actor name matching now ignores full-width (ideographic) spaces everywhere, not just in the torrent sorter, so "山田　花子" matches "山田 花子" and "山田花子" when linking cast, merging and importing actors (#34).
- Upgraded to .NET 11 RC2 (`11.0.0-rc.2.26504.105`), including EF Core and the `dotnet-ef` tool (#53).

### Fixed

- The Scenes timeline's hover preview on the movie detail page no longer plays forward by itself; its frame follows only the pointer position (#69).
- The System page's uptime no longer jumps by an hour when daylight saving time starts or ends while the app is running.
- Task durations of a day or more on the System > Tasks page show their days (`1d 01:00:00`) instead of wrapping back to `01:00:00`.

## [0.2.4] - 2026-10-11

### Changed

- Apex cards on the Scenes wall show a screenshot, like highlights, and play their preview only while hovered. Before, every card in the window loaded and decoded its own preview video, which stalled scrolling on the Apexes view. Screenshots of existing apexes are generated in the background the next time the wall shows them, and the card shows its generating placeholder until then (#33).
- Hovering a card on the Scenes wall or an actor's clip sections no longer goes through the server: the preview now plays in the browser, after the pointer has rested on the card for a moment. Before, every card the pointer crossed (including cards sliding under a still pointer while you scrolled) cost a round trip and a re-render, and started its preview download (#33).

## [0.2.3] - 2026-10-11

### Fixed

- The Scenes, Highlights and Apexes wall loads each window of cards much faster, so scrolling it no longer outruns the loading (#33). Opening a window used to run dozens of queries per movie on it. It now loads all of them together.

### Removed

- The no-op service worker, which Chrome flagged as adding navigation overhead. Installing the app from Chrome's menu still works; browsers that already have the worker drop it on their next update check.

## [0.2.2] - 2026-10-10

### Fixed

- Movie Detail's Genres editor: hovering a tag that comes from scenes, highlights or apexes now explains that removing it also removes it from them; before, the tooltip could be missing.
- Movie Detail's Genres editor: an actor tag left on the movie by an earlier version is now removed directly, without the scenes-and-highlights confirmation that didn't apply to it.
- Removing a tag from a movie together with its scenes, highlights and apexes either completes in full or changes nothing.

## [0.2.1] - 2026-10-10

### Changed

- Actor tags no longer become plain tags on the movie: they stay on their actors, so they no longer appear in Movie Detail's Genres, `MetaGenres` or the `.nfo`. The Movies grid's actor-tag filter, the Scenes wall and the cast pills work as before. A leftover link from an earlier version is dropped by Apply Rules to Library or the next edit of that movie's clips.

### Fixed

- Movie Detail's Genres editor: tags the movie only has because a scene, highlight or apex carries them showed a remove button that did nothing. They now show dashed and muted while editing, and removing one asks for confirmation, then removes the tag from those scenes, highlights and apexes too.

## [0.2.0] - 2026-10-10

### Changed

- The Movies page's Genre filter now nests subtags under their parent, like the Actor tag filter and the Scenes wall.
- Docker image: MediaInfo now comes from the `MediaInfo.Wrapper.Core` NuGet package (bumped to 26.10.0, which ships generic Linux runtime identifiers) instead of the distro's `libmediainfo0v5`; only its `libcurl3-gnutls` and `libmms0` dependencies are installed from apt.

### Fixed

- Clip tag refreshes and actor discovery imports now retry or skip only on a duplicate-key database error, not on any constraint error (a foreign key or NOT NULL failure now surfaces as the real error).
- Screen readers now announce an actor's tags (and where an inherited actor comes from) when the actor gets focus on a scene, highlight or apex row or card.
- Deleting a tag that has subtags on Movies › Tags (the main list or Actor Tags) now shows an error message instead of breaking the page's connection.
- Removing an actor from a scene, highlight or apex now also removes their actor tags on it, so the Scenes wall's tag filter no longer matches a clip the tagged actor has left, and the movie drops a plain tag only that actor's tag gave it.
- Deleting, ignoring, renaming, merging or re-parenting a tag, or making a tag an actor tag, now re-checks the affected movies for `.nfo` drift (in the background, so a tag on hundreds of movies doesn't block the Tags page).
- Two refreshes of the same movie's clip tags running at once no longer fail on a duplicate tag link (they now run one at a time).
- In the video player's VR 2D mode, the projection picker now fades out with the scrub bar and header when the player is idle, and comes back with them.
- The VR 2D player no longer has a second fullscreen button over the video; the player's own fullscreen button (and the F key or a double-click) does the job.
- Movie Detail's Delete dialog no longer offers "Delete files from disk" for a movie with no files on disk, such as a wanted movie.

### Added

- Actor tags: tag one actor within a movie, scene, highlight or apex (blonde or brunette, say) from Movie Detail's cast section and the scene editor, and filter by them on the Movies grid and the Scenes wall, alone or together with an actor ("Mei, brunette"). They flow down from the movie to scenes, highlights and apexes per actor, and roll up like other clip tags. A movie also gets each actor tag as a plain tag, so they reach its genres and `.nfo`. They nest one level (hair › long), and a parent tag matches its subtags in the filters. Manage them under **Movies > Tags > Actor Tags**, laid out like the main tags (search, sort, create, set parent, rename, delete, filter the Movies grid by one); they can't be added to a movie or clip as plain tags or merged. An existing genre ("Blonde" from metadata) becomes an actor tag from the main tags list, as long as no scene, highlight or apex carries it: it leaves its movies' genres, since a genre doesn't say which actor it describes, and an actor tag nothing uses becomes a plain tag again. On Movie Detail they show as small colored pills under each cast member (the first two, then "+N"), with all of them, and those only the movie's clips carry, in a hover popover (hovering an actor on a scene row or an "All scenes" card shows their tags too); **Edit Cast** adds a "+ tag" pill with the same search box as the movie's tags.
- The VR 2D player now opens automatically, with the matching projection, for a movie whose VR format is VR180 SBS or Fisheye SBS (in the video player and the Review card). The VR 2D button still turns it off.
- Getting started: a "Folder permissions" section covering the non-root container user, checking and fixing mount ownership, and common access errors.
- Add a movie: a spinner and "Searching for metadata…" status show while the metadata lookup runs.

## [0.1.2] - 2026-10-08

### Fixed

- Docker ARM64 image: publish the app assembly for ARM64 instead of defaulting to x86-64.

## [0.1.1] - 2026-10-08

### Changed

- Docker image: MediaInfo now comes from the distro's `libmediainfo0v5` package instead of native libraries copied out of the NuGet cache.
- Docker image: FFmpeg is now a minimal build from the upstream release source (decode everything; encode only WebP and VP9), replacing the full static build.
- Docker image: now based on the chiseled `aspnet` image (no package manager), with the native libraries it needs copied in and a small busybox shell for `docker exec <container> sh`. Together with the FFmpeg change, the image shrinks from about 940 MB to about 478 MB.
- Docker image: published images now carry an SBOM and build provenance attestations. FFmpeg itself is built from source, so it does not appear in the SBOM.

### Added

- Docker image for `linux/arm64`, published as one multi-architecture tag alongside `linux/amd64`.

## [0.1.0] - 2026-10-06

First release.

### Added

- Movie tracking with Missing / Downloading / Got status. Add by code, import folders already on disk, or add from Discover. Movies grid with search, faceted filters and sorting, and a Missing list.
- Movie detail: metadata and cover-crop editing, cast and tags, media info, multiple file versions, VR / 3D format detection, and `.nfo` viewing, editing and conflict resolution.
- Review and Cleanup modes to go through the library one movie at a time, and Discover for new and upcoming studio releases.
- Tag library with replacement and ignore rules.
- Actor directory with profile data from javinizer-go, r18.dev, WAPdB and minnano-av, per-actor missing releases, photo galleries, merging and aliases.
- Built-in browser player with local or Jellyfin trickplay, scenes, highlights and apexes, a Scenes wall, VR 2D viewing, and optional DeoVR support.
- Prowlarr search, qBittorrent queue and history, and the Torrent sort wizard that organizes finished downloads into the library, including multi-part VR merge.
- Jellyfin ownership sync and "Open in Jellyfin" links.
- System pages: status and health checks, scheduled background tasks, and library statistics.
- Docker image with FFmpeg and MediaInfo bundled, configured through the UI or environment variables.
