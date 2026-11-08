# Changelog

All notable changes to this project are documented in this file, in [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format. This project adheres to [Semantic Versioning](https://semver.org/) (pre-1.0: breaking changes may land in a minor bump).

## [Unreleased]

### Added

- A public **REST API** under `/api/v1` (look codes up, list and read movies, add a movie, edit metadata with `set`/`clear`, add or remove tags and cast, preview and push the `.nfo`, search Prowlarr and grab a release by its guid) with an OpenAPI document at `/openapi/v1.json`, and an **MCP server** at `/mcp` (stateless Streamable HTTP) with tools for searching movies and editing their metadata, tags, cast and `.nfo`, for use from Claude Code and other MCP clients. Neither is authenticated, like the UI, so don't expose them to the internet. Browser requests are accepted only from Javbuddy's own origin or the ones listed in the new `Api__AllowedOrigins` setting. See [docs/api.md](docs/api.md).
- **Crop Cover** can rotate the image: **Rotate left** and **Rotate right** turn the source 90° at a time, and a **Straighten** slider (±45°, 0.1° steps) fine-tunes a crooked scan. The crop box can't leave the rotated image, so no empty corner is saved. A 90° turn resets the crop box and zoom, and a move of the slider resets the crop box but keeps the zoom (the chosen aspect ratio is kept either way). The straighten angle is applied when the cover is saved, after any 90° turns. A photo's EXIF orientation is now applied to the image before it is rotated or cropped, so the crop matches what the cropper shows.
- A newly added movie (added by code, or imported by a library rescan) whose `.nfo` has no runtime now gets its runtime from the video file's duration, if MediaInfo is on and the scan succeeds at that moment. An existing runtime is never replaced, and a folder without an `.nfo` is left alone so an `.nfo` added later is still imported. The same fill applies when a finished torrent is sorted into the library, on a manual rescan that finds a missing movie's files, and on **Refresh from local**.

### Changed

- **Crop Cover** has a leaner layout: image sources are a strip of small thumbnails (backdrop and poster shown as icons, gallery images numbered with no "Gallery" prefix) with a single icon-only **Upload** button that offers both a file and a URL, the controls sit in one toolbar under the image, the rotate buttons are icons, and the drag hint moved to an (i) in the header. **Reset** now also clears **Straighten** and undoes 90° turns, moving **Straighten** keeps the zoom, the zoom slider follows the mouse wheel, a spinner shows while the image rotates or loads, and the controls are locked while it does. The **Change Portrait** editor for actors shares the same layout (toolbar under the image, (i) hint in the header, no preview caption, spinner while the image loads), keeping its avatar previews and its portrait-only actions.
- Runtime is no longer editable: the movie metadata editor drops its Runtime field, and the torrent-sort metadata editor shows it read-only. It comes from the scraped or `.nfo` value, or from the video's duration when missing.

### Fixed

- Syncing to the `.nfo` now creates the elements a file lacks (or has empty) — Overview (`<plot>`), original title, director, studio, label, series, release date and runtime — and the passive drift check reports them, so a value added or fetched in Javbuddy after the import reaches the `.nfo`. After upgrading, the first rescan may show drift on many movies whose `.nfo` lacks a value Javbuddy has; **Push** writes them.

## [0.5.1] - 2026-11-08

### Added

- **Play all** and **Shuffle** buttons on the Movies page, next to Options, play every Got movie with a local video file from the current filter results, in the grid's order or at random, in the player. In the player, arrows on the scrub bar (and the N / P keys) go to the next or previous movie; the last movie's Next closes it. Both are disabled when nothing matches or the Missing filter is on. (#122)
- A library rescan now queues the artwork a newly imported movie came without, generated in the background from frames of its own video once MediaInfo has scanned it (a restart or a cancelled run picks up what is left): 10 extrafanart stills when it has none, and a poster and fanart image when it has neither. Each is a toggle (on by default) in **Settings → Metadata → New movie artwork**, or the `NewMovieArtwork__GenerateExtraFanart` / `NewMovieArtwork__GeneratePosterAndFanart` environment variables. Existing images are never replaced, and a movie whose metadata already has a cover URL gets no generated poster. (#118, #119)
- The Movies page's **Content Type** filter can now exclude a type too: click a type to include it, again to exclude it, a third time to clear it. (#127)
- The Actors page can filter by release type (a Content Type group like the Movies page's: click a type to include actors with such a movie, again to exclude them) and sort by the number of movies of each type, with matching optional table columns and a count badge on grid cards while sorting by one. Counts cover all tracked movies. (#127)
- **Generate stills** can now be stopped while it is running: the Cancel button becomes **Stop**, which aborts the frame extraction and returns the dialog to idle. (#125)

### Fixed

- Closing the video player right after it opens no longer ends the page's session when a late subtitle call hits the already-closed player.
- Moving to another movie in the player (Movies' Play all / Shuffle, or switching versions) now starts a fresh video element with its own subtitle tracks instead of reusing the previous one's, so the last movie's subtitle line no longer stays on screen. (#122)

### Changed

- **Generate poster and fanart** now writes `poster.webp` and `fanart.webp` instead of JPEG files, and takes side-by-side VR frames from the left eye only.
- The default WebP quality for generated extrafanart stills is now 92 (was 85), in the Generate stills dialog and for the stills generated automatically for new movies. (#120)
- The Generate stills dialog shows a spinner on the Save button and a "Saving N images…" bar while the ticked stills are being saved (kept visible for at least 0.7 s, and no longer held back by the encoding work). (#121)
- The Generate stills dialog closes itself once the stills are saved, leaving the new images in Movie Detail's gallery; a failed save keeps it open with the error. (#123)
- **Settings → Metadata → Movies** now has a single **Local Library** tile. MediaInfo, Trickplay and New movie artwork moved into its drawer, and the tile summarizes which of them are on. (#124)

## [0.5.0] - 2026-11-07

### Added

- Movie Detail's **Tools** menu has a **Generate stills** action (for movies with a scanned local video). It splits the video (minus its first and last 10%) into slots, keeps the sharper, better-exposed of two frames per slot, and shows each still in a preview the moment it is ready, with a shimmer in place of the ones still being generated. Tick which to keep and they are saved as WebP `fanartN` images in the movie's `extrafanart` folder. You choose how many (1–20, default 10), the WebP quality, whether to add to, replace, or only generate when there are no existing images, and whether to crop side-by-side VR video to the left eye (ticked by default for VR movies). The Add Images dialog also offers it. (#104)
- The minnano-av.com actor search now accepts an actress ID (`141240`) or a full `actress<id>.html` URL (query string ignored) as well as a name, and opens that actress directly. (#114)
- The minnano-av.com actor search modal has an **Image (portrait)** row that imports the performer's image from minnano-av.com as the actor's portrait, replacing an existing one. It is ticked by default only when the actor has no portrait; if the download fails, the other fields still import and the modal shows a warning. (#116)
- The Prowlarr search modal now warns, naming each indexer, when Prowlarr skipped one because it is disabled or was temporarily blocked after repeated failures; System → Status lists the same indexers in its Health section. Nothing is shown when Prowlarr is unconfigured or unreachable. (#117)

### Changed

- The player's subtitle button is now a closed-captions icon that opens a menu: pick Off or a language (checkmark on the active one), the subtitle size (Small to Extra large) and the background (Transparent, Semi-transparent, Solid). Subtitles now default to a transparent background with a text outline. Whether subtitles are on, their size and background are remembered in the browser, so turning them off stays off for the next clip in a playlist or shuffle. (#108, #109, #110, #111)
- The minnano-av.com search modal title now separates the site name and actor name with a colon instead of an em dash. (#115)

### Removed

- **Breaking:** the WAPdB (warashi-asian-pornstars.fr) actor metadata source is gone: its Settings → Metadata tile and batch enrichment, its search modal, the combined "search all providers" modal, the Search button's provider dropdown (Search now opens the minnano-av.com modal directly) and the `Warashi__*` environment variables. Existing actors, imported fields, photos and the "WAPdB" photo albums are untouched; only the stored WAPdB settings are dropped. (#112)

### Fixed

- A subtitle cue no longer stays on screen over the next clip or movie when that one has no subtitles. (#107)
- NFO sync now adds a `<title>` to an `.nfo` that lacked one (or had it empty) when the movie has a title, placing it right after `<id>` (else as the first element). (#106)
- minnano-av.com actor detail pages whose performer name or aliases include a parenthesized qualifier (such as `みさ（同人モデル）` or `みさちゃん（FC2）`) now parse and keep the qualifier instead of failing to load or truncating at the first parenthesis. (#105)
- When minnano-av.com cannot be reached (for example, `Connection refused`), the actor search modal now shows an inline connection error instead of reporting "No matching performers found", and the server logs a single-line warning instead of a full stack trace. (#105)
- Clicking **Inspect** on an actor search result now shows a spinner on the button and a "Loading performer details…" placeholder while the details load. (#113)

## [0.4.3] - 2026-11-05

### Added

- The player shows external subtitles: `.srt` and `.vtt` files beside the video that are named after it (`ABC-123.srt`, `ABC-123.en.srt`; the suffix is the language label) are offered as tracks, the first on by default. The **CC** button over the video steps through the tracks and turns them off. SRT is converted to WebVTT on the fly; nothing is written to the library. `.ass`/`.ssa` and the VR viewer are not supported.

### Fixed

- The first search on minnano-av.com or WAPdB through a challenge solver (seen with a freshly started Trawl) no longer reports "blocked by a bot challenge": the solver sometimes returns a still-challenged page on a cold start, and Javbuddy now retries it once before giving up. (#103)
- In the player's scene editor, the keyboard shortcuts (i) popover is drawn over the **Scenes** heading and the add buttons instead of under them.
- Tags added in Javbuddy to a movie whose `.nfo` had no `<genre>` elements are now detected as `.nfo` drift ("Javbuddy changed") and are written into the file when you resolve the conflict or use **Write Javbuddy metadata to .nfo**; before, an `.nfo` without genres was never flagged and nothing was ever written to it. The `<genre>` elements are placed before the first `<actor>` with the file's own indentation. An `.nfo` with no genres is also flagged for a movie that already had tags, so after updating, such movies show up as drift until you write them. Likewise, if another tool strips the genres from an `.nfo` whose tags Javbuddy knows, it shows as an external edit.

## [0.4.2] - 2026-11-04

### Added

- A movie has a **Content Type**: Censored (the default), Uncensored or Decensored. Set it in the metadata editor; a movie first added under a date-coded uncensored studio code (`carib-010120-001`, `1pondo-010120_001`, …) or a `tokyo-hot-` code starts as Uncensored. The movie's detail page shows it next to the status, and clicking it lists every movie of that type. The **Content Type** filter on the Movies page, Actor Detail and the DeoVR group editor, and on the Scenes, Highlights and Apexes walls (which filter by the content type of their movie), keeps movies of any selected type. The value stays in Javbuddy's database and is not written to the `.nfo`.

## [0.4.1] - 2026-11-04

### Fixed

- **Generate poster & fanart** no longer fails with "Couldn't extract any frames from the video" in the Docker image, whose ffmpeg has no JPEG encoder: frames are now taken as WebP (with the same colour conversion the scene screenshots use) and converted to JPEG by the app. When extraction does fail, the message now says why.

## [0.4.0] - 2026-11-04

### Added

- Support for movies that come without metadata or artwork, such as uncensored releases. A folder with no `.nfo` is tracked with its code and the probed video details, and the Movies filter has a **Metadata incomplete** option that lists them. Saving the metadata editor on such a movie creates its `.nfo` (Tools → **Create .nfo** does the same without editing); an existing `.nfo` is never overwritten. Tools → **Generate poster & fanart** picks sharp, well-exposed frames from the video and writes `poster.jpg` (a 2:3 crop) and `fanart.jpg`, skipping any image that already exists.
- Date-coded uncensored release codes (`carib-010120-001`, `1pondo-010120_001`, `10musume-…`, `paco-…`, `muramura-…`, `tokyo-hot-n1234`) are read in full from folder and file names instead of being cut after the date.
- System → Status lists the **Challenge solver** under Sub-services once one is configured, with a live connection check, its version and a link to it.

## [0.3.10] - 2026-11-01

### Added

- The Actor Photos wall has a **Size** option (Small, Medium, Large) that changes the photo row height.
- **Challenge solver** (Settings → Connections): point Javbuddy at a [FlareSolverr](https://github.com/FlareSolverr/FlareSolverr) or compatible service such as [Trawl](https://github.com/germondai/trawl) to get minnano-av.com and WAPdB past a Cloudflare challenge, which until now looked like "no results". Once a solver is configured, minnano-av.com and WAPdB use it by default (each can be switched off on Settings → Metadata, or with `MinnanoAv__UseChallengeSolver` / `Warashi__UseChallengeSolver`); a source asks the site directly first and only repeats a request through the solver when the site answers with a challenge. A challenge that cannot be passed is now reported instead of an empty result list: the search modals say the site is protected by a bot challenge and point to the challenge solver setting, and a batch enrichment's summary counts the actors "blocked by Cloudflare". `FlareSolverr__BaseUrl` (and `FlareSolverr__MaxTimeoutMs`) set the solver from the environment.
- **System → Backups** backs up the database (not the image cache, the actor photos and trickplay in the object store, or the r18.dev data). Automatic backups run on a schedule (every day by default; the first one is made shortly after a start when none has run yet) and are deleted after a retention period (10 days by default; manual backups are kept); **Backup Now** makes one on demand. Each backup is a zip with a consistent snapshot of the database and a small manifest, listed with its Javbuddy version, size and movie, actor, tag and scene counts. **Restore** stages a backup and applies it on the next start (after **Restart now**, which needs a restart policy such as Docker's `restart: unless-stopped`), keeping the replaced database as a pre-restore backup; **Export** downloads a backup and **Import** adds one from another installation. Backups live in a `backups` folder next to the database (`Backup__Path` moves it), and `Backup__Enabled`, `Backup__FrequencyDays` and `Backup__RetentionDays` override the settings.

## [0.3.9] - 2026-10-31

### Added

- A weekly **Database Maintenance** task (System → Tasks) refreshes the database's query planner statistics and, once free pages reach 10% of the file and the disk has room for the rebuild, vacuums it, returning the space deleted rows leave behind to the disk.

### Changed

- The FFmpeg bundled in the Docker image is upgraded from 9.0.1 to 9.0.2.
- Filtering for favorite movies, scenes and actors, or retired actors, uses smaller indexes covering only those rows, and the unused torrent-hash index is dropped.
- Importing or enriching an actor no longer adds an alias another actor already has, matching what adding an alias by hand already enforced.

### Fixed

- Tag names, actor names and actor aliases are now unique ignoring case in the database itself, not only in the app's checks, and two actors with the same single name (no last name) can no longer both be saved. Any such duplicates in an existing database are kept but renamed with the first free " (2)", " (3)"… suffix during the upgrade, so they can be merged with the Merge tools (a renamed actor keeps its old name as an alias, so its movies stay linked); an alias an actor has twice in different case is removed.
- Duplicate checks for tag and actor names, aliases, ignored tags and replacement rules no longer miss a name with non-ASCII letters (e.g. "ū") that matches only in different case.
- Movie codes are now case-insensitive in the image cache, the torrent history and actor photos too, as they already were for movies, so an image cached under `abc-123` is found and replaced for `ABC-123` instead of being duplicated. The upgrade fails on a database that already holds the same code twice in different case in the image cache or the candidate list; delete the database file to recreate it.

## [0.3.8] - 2026-10-26

### Added

- The Movies, Actor Detail and DeoVR filters gain Release year, Runtime, Bitrate and File size sliders, and the Actor section gains a Cast size slider (how many linked actors a movie has). Features gains "Has favorite actor" and "Has retired actor".
- Genre, Studio, actor Name and Actor tag filter options can be excluded: clicking an option includes it (✓), clicking again excludes it (✕, struck through), and a third click clears it. Excluding is not offered for Studio while browsing all r18.dev releases.
- New sorts: Code (natural order ignoring case, so ABC-2 comes before abc-10), Bitrate, Scenes, Highlights, Apexes and Cast size.

### Changed

- The Favorites sort is now Date favorited: favorites first, most recently favorited first (or oldest first when reversed), then everything else by code. Actor Detail now honors it too, where it previously fell back to Added.
- The Filter menu's NFO drift group now sits inside Features, below the feature options.

### Fixed

- Long actor tags, and tags under a parent, are no longer cut off in the cast pills on Movie Detail or in the actor-tag hover popovers; they wrap instead.

## [0.3.7] - 2026-10-25

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
