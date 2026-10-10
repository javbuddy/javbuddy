# Codebase Map

A concrete, current-as-of-writing map of where things actually live, for the "where is X implemented" lookups `docs/feature-workflow.md` step 2 delegates to a subagent. `docs/architecture.md` covers layering *rules* and *why*; this doc is the file-tree index those rules apply to.

**This doc goes stale fast.** When you add, rename, or remove a page, shared component, service file/subfolder, or test, update the relevant row in the same change — don't leave it for a later pass.

## Pages (`Javbuddy/Components/Pages/`)

One row per product area.

| Area | Page(s) | Covers | Tests |
|---|---|---|---|
| `Area::Movies` | `Movies.razor` (+ `MoviesSections/MovieReleasesBrowser.razor`, `R18DevCatalogFilterMenu.razor`, `MoviePosterOptionsModal.razor`; grid state in `MoviesSections/MovieGridFilterState.cs`, `MovieGridWindow.cs`, `MoviePosterOptions.cs`, `MoviesPersistedState.cs`), `MovieAdd.razor`, `MovieDetail.razor` (+ `MovieDetailSections/NfoEditor.razor`, `MediaInfoModal.razor`, `MovieScenesSection.razor` — Movie Detail's scenes timeline/highlights shelf/scene grid — and `MovieGenresSection.razor`, `MovieCastSection.razor`, `MovieImagesSection.razor`, `MoviePreviewDetails.razor`, `R18DevRelatedReleasesShelf.razor` — "More from this series" — `MovieDetailText.cs`), `MovieCleanup.razor` (+ `MovieCleanupSections/MovieCleanupSummary.razor`, `MovieCleanupCastTags.razor`), `MovieDiscover.razor` (+ `MovieDiscoverSections/DiscoverSectionWindow.cs` — each studio section's VirtualizedGrid window) | Tracked-movie grid w/ search & filter (+ "All releases (r18.dev)" catalog browse with its own filters and library status), add-by-code wizard w/ r18.dev code suggestions, full detail view w/ Jellyfin/javinizer-go actions, one-at-a-time keep/remove review queue (+ no-delete Review mode, `/movies/review`), new studio releases to add or dismiss, per studio | `Javbuddy.Tests/Components/Pages/{MoviesTests,MovieReleasesBrowserTests,MovieAddTests,MovieDetailTests,MovieCleanupTests,MovieDetailSections/MovieScenesSectionTests,MovieDetailSections/MovieDetailTextTests,MoviesSections/MovieGridWindowTests,MoviesSections/MovieGridFilterStateTests,MoviesSections/MoviePosterTextTests,MovieDiscoverTests,MovieDiscoverSections/DiscoverSectionWindowTests}.cs`; `Javbuddy.E2ETests/SmokeTests/{MoviesPageTests,MovieDetailPageTests,MovieCleanupPageTests}.cs`; `Flows/{AddMovieFlowTests,MoviesBrowseAllReleasesFlowTests,NfoEditorFlowTests,MovieDetailActionsFlowTests,MoviesGridScrollTests,SearchAndGrabFlowTests,MovieCleanupFlowTests,MovieCleanupVrModeFlowTests}.cs` |
| `Area::Actors` | `Actors.razor` (+ `ActorsSections/ActorListFilter.cs`, `ActorCardText.cs`), `ActorAdd.razor`, `ActorDetail.razor` (+ `ActorDetailSections/ActorClipsSection.razor` — its Scenes/Highlights/Apexes sections — and `ActorDetailSectionOrder.cs`, the Settings > UI section order), `ActorImport.razor`, `ActorMissing.razor`, `ActorPhotos.razor` | Tracked-actor grid, add, detail, local-library actor import, actors missing from owned movies, uploaded photo timeline | `Javbuddy.Tests/Components/Pages/{ActorMissingTests,ActorMissingFilterTests,ActorsTests,ActorsSections/ActorListFilterTests,ActorDetailTests,ActorDetailSections/ActorClipsSectionTests,ActorDetailSections/ActorDetailSectionOrderTests,ActorImportTests,ActorEditTests,ActorPhotosTests}.cs`; `Javbuddy.E2ETests/SmokeTests/{ActorsPageTests,ActorDetailPageTests,ActorMissingPageTests}.cs`; `Flows/{ActorAddFlowTests,ActorPhotosLoadingTests}.cs` |
| `Area::Missing` | `Missing.razor` | Grid of movies marked Missing, w/ status toggle | `Javbuddy.E2ETests/SmokeTests/MissingPageTests.cs` — no bUnit coverage |
| `Area::Settings` | `Settings.razor`, `SettingsConnections.razor` (+ `SettingsConnectionSections/{Javinizer,Jellyfin,Prowlarr,QBittorrent}ConnectionSection.razor`, `PathMappingsSection.razor`), `SettingsMetadata.razor` (+ `SettingsMetadataSections/LocalLibrarySection.razor`), `SettingsDeoVr.razor`, `SettingsUi.razor` | Settings hub/router; per-integration connection config; local library folders and optional metadata sources (r18.dev, WAPdB); DeoVR integration and its groups; UI theme, Movie Detail backdrop, Actor Detail section order, player apex countdown | `Javbuddy.Tests/Components/Pages/SettingsConnectionSections/{SectionSmokeTests,PathMappingsSectionTests}.cs`, `Javbuddy.Tests/Components/Pages/SettingsMetadataSections/LocalLibrarySectionTests.cs`, `Javbuddy.Tests/Components/Pages/SettingsMetadataTests.cs`, `Javbuddy.Tests/Components/Pages/SettingsDeoVrTests.cs`, `Javbuddy.Tests/Components/Pages/SettingsUiTests.cs`; `Javbuddy.E2ETests/SmokeTests/SettingsPageTests.cs`; `Flows/SettingsConnectionsFlowTests.cs` |
| `Area::TorrentSort` | `TorrentSort.razor` | Torrent file browser, batch scraper, merge/organize wizard | `Javbuddy.Tests/Components/Pages/TorrentSortMergeStepTests.cs`; `Javbuddy.E2ETests/SmokeTests/TorrentSortPageTests.cs` |
| `Area::LibraryImport` | `LibraryImport.razor` | Scan and import movies already on disk | `Javbuddy.E2ETests/SmokeTests/LibraryImportPageTests.cs`; `Flows/LibraryImportFlowTests.cs` — no bUnit coverage |
| `Area::Activity` | `ActivityQueue.razor`, `ActivityHistory.razor` | Live torrent download queue; historical activity log (Downloads tab, plus a Deleted movies tab at `?view=deleted` to review and purge deleted-movie history) | `Javbuddy.Tests/Components/Pages/{ActivityHistoryTests,ActivityQueueTests}.cs`; `Javbuddy.E2ETests/SmokeTests/{ActivityHistoryPageTests,ActivityQueuePageTests}.cs`; `Flows/{ActivityQueueInteractionTests,MoviesBrowseAllReleasesFlowTests}.cs` |
| `Area::System` | `SystemStatus.razor`, `SystemTasks.razor` | Integration health checks; on-demand background task runner | `Javbuddy.Tests/Components/Pages/SystemStatusTests.cs`; `Javbuddy.E2ETests/SmokeTests/{SystemStatusPageTests,SystemTasksPageTests}.cs`; `Flows/{SystemStatusHealthFlowTests,SystemTasksRunNowFlowTests}.cs` |
| *(no Area — framework pages)* | `Error.razor`, `NotFound.razor` | Global error boundary; 404 fallback | `Javbuddy.E2ETests/SmokeTests/ErrorPagesTests.cs` |

## Cross-cutting UI (`Javbuddy/Components/Layout/` and `Components/Shared/`)

`Layout/` holds the app shell, not tied to any one Area: `MainLayout.razor` (page chrome), `NavMenu.razor` (left nav — tested by `Javbuddy.Tests/Components/Layout/NavMenuTests.cs`), `SearchBox.razor` (header search, backed by `SearchService`, plus r18.dev catalog matches from `R18DevReleaseBrowseService`), `ReconnectModal.razor` (+ its own `.js`, SignalR circuit-drop overlay).

`Shared/` holds reusable UI patterns — check here before building a new one:

| Component | Provides | bUnit test |
|---|---|---|
| `BackButton.razor` | Standard "‹ Back" link | `BackButtonTests.cs` |
| `CropperViewport.razor` (+ `.razor.js`) | The image-crop viewport (image, crop box, eight handles) and the shared JS cropper core (`ImageCropper`, `createCropperSession`) that `MovieCoverCropModal` and `ActorImageUploadModal` build on | `CropperViewportTests.cs` |
| `ConnectionSettingsCard.razor` | Card UI for one integration's connection status/test/save form | `ConnectionSettingsCardTests.cs` |
| `DropdownMenuButton.razor` | Button that opens a dropdown menu of actions | `DropdownMenuButtonTests.cs` |
| `LoadingIndicator.razor` | Spinner/placeholder shown while data loads | `LoadingIndicatorTests.cs` |
| `CastSearchAdd.razor` | Actor search box + results list for adding a tracked actor to a movie's cast; restylable via `--cast-search-*` CSS custom properties | `CastSearchAddTests.cs` |
| `TagSearchAdd.razor` | Tag-library search box + results list for adding an approved tag to a movie; restylable via `--tag-search-*` CSS custom properties | `TagSearchAddTests.cs` |
| `MovieGallery.razor` | Thumbnail gallery for extrafanart/preview images, opening `GalleryLightbox` | `MovieGalleryTests.cs` |
| `GalleryLightbox.razor` | Full-screen, navigable image viewer (progress bar, prev/next, keys, swipe, Original toggle, zoom) shared by `MovieGallery`, `ActorPhotos` and `ActorPhotoGallery` | `GalleryLightboxTests.cs`; real-browser coverage in `GalleryLightboxNavZoneFlowTests.cs` and `GalleryLightboxZoomFlowTests.cs` |
| `ImageZoomControls.razor` | Lightbox zoom buttons plus wheel/double-click/drag/pinch zoom and pan (`.razor.js`), used by `GalleryLightbox` and `ImageLightboxModal` | `ImageZoomControlsTests.cs`; real-browser coverage in `GalleryLightboxZoomFlowTests.cs` |
| `VrViewer.razor` (+ `.razor.js`) | Best-effort "VR 2D" mode for a side-by-side VR `<video>`: a WebGL canvas that unwarps the left eye (equirectangular or fisheye 180°) through a draggable virtual camera; switches the video to CORS mode itself | `VrViewerTests.cs`; real-browser coverage in `Javbuddy.E2ETests/Flows/MovieCleanupVrModeFlowTests.cs` |
| `VideoPlayerColumn.razor` | The `<video>` with VR, arrow-key seeking and the `ScrubBar` (or the host's `Controls`) under it, shared by `VideoPlayerShell` and the Review/Cleanup card | `VideoPlayerColumnTests.cs` |
| `ActorPhotoGallery.razor` | Actor-detail Photos card: album filters, gallery management, and portrait promotion | `ActorPhotoGalleryTests.cs` |
| `ClipCard.razor` (+ `.razor.js`) | One scene, highlight or apex card (screenshot/hover preview, duration, favorite, apex count, title, movie, actors, tags) that plays its clip on a plain click; shared by the Scenes wall and Actor Detail's clip sections. `preventPlainCardClicks` in its JS keeps a plain click from following the card's link | `ClipCardTests.cs` |
| `ActorPhotoUploadModal.razor` | Batch actor-photo upload and optional album creation | `ActorPhotoUploadModalTests.cs` |
| `MovieImageUploadModal.razor` | Movie Detail's "Add Images": multi-file upload / drag & drop or one URL import into the movie's local `extrafanart` folder | `MovieImageUploadModalTests.cs` |
| `DeleteConfirmDialog.razor` | Modal "Delete "X"?" confirmation (Cancel/Esc/backdrop cancel without closing a surrounding player modal; Cancel focused on open), used by the scene, highlight and apex editors and Movie Detail's image delete | `DeleteConfirmDialogTests.cs`; real-browser coverage in `SceneDeleteConfirmFlowTests.cs`, `HighlightFlowTests.cs` |
| `ClipEditor.razor` (+ `.razor.js`) | A movie's scenes, highlights and apexes as one tree (built by `ClipTree`): scene rows and form, chapter import, the Detect / Write chapters row, the player bridge and the M / Shift+M, H / Shift+H and A shortcuts; rolled-up tags and inherited actors show grayed. Shared by `MovieCleanup.razor`'s Review card and `VideoPlayerModal`'s side panel | `ClipEditorSceneTests.cs`, `ClipEditorHighlightTests.cs`, `ClipEditorApexTests.cs`, `ClipEditorTreeTests.cs` (on `ClipEditorTestBase`); real-browser coverage in `ClipEditorFlowTests.cs`, `HighlightFlowTests.cs` |
| `ClipHighlightRow.razor` / `ClipHighlightForm.razor` | A highlight's row and add/edit form inside `ClipEditor` | via `ClipEditorHighlightTests.cs` |
| `ClipApexRow.razor` / `ClipApexForm.razor` | An apex's row and add/edit form inside `ClipEditor`, with the previous apex's tag suggestions | via `ClipEditorApexTests.cs` |
| `ClipActorChips.razor` (+ `ClipForms.cs`) | A highlight's or apex's actor chips: inherited ones dimmed until a toggle makes the set its own, **Use inherited** to go back | via `ClipEditorHighlightTests.cs`, `ClipEditorApexTests.cs` |
| `ImplicitChips.razor` (+ `ImplicitChip.cs`) | Grayed, read-only chips for rolled-up tags and inherited actors, with where they come from; used by `ClipEditor`'s rows and `MovieTagsEditor` | `ImplicitChipsTests.cs` |
| `ActorTagsEditor.razor` | One row per actor with their actor tags as chips (own removable, inherited dashed, rolled-up grayed), add from the actor-tag library (new ones are created on the Tags page); applies at once through `IActorTagService`. Used under `ClipEditor`'s scene, highlight and apex edit forms | `ActorTagsEditorTests.cs` |
| `ActorTagHover.razor` (+ `ActorTagPillStyle.cs`) | Wraps an actor's name or chip: hovering or focusing opens a top-layer popover (placed by `ActorTagHover.razor.js`, which `App.razor` loads once) with where an inherited actor comes from and their actor tags; used by `ClipEditor`'s scene rows (via `ImplicitChips`) and Movie Detail's scenes section | `ImplicitChipsTests.cs`, `MovieScenesSectionTests.cs`; real-browser coverage in `ActorTagsFlowTests.cs` |
| `MovieDetailSections/ActorTagPills.razor` | A cast member's movie-level actor tags as small colored pills under their name (first two, then "+N"), a hover popover with all of them plus the clip-derived ones, and under Edit Cast a "+ tag" pill and remove × | `ActorTagPillsTests.cs`; real-browser coverage in `ActorTagsFlowTests.cs` |
| `RedundantMark.razor` | Amber "redundant" / "same as inherited" badge on an explicit tag or actor set that repeats what rolls up or is inherited; used by `ClipEditor`'s scene form, `ClipHighlightForm`, `ClipActorChips` and `MovieTagsEditor`. Dark by default; a light host sets `--redundant-mark-fg` | via `ClipEditorTreeTests.cs`, `ClipEditorHighlightTests.cs`, `MovieTagsEditorTests.cs` |
| `ApexMarkers.razor` | Diamond apex markers drawn above `ScrubBar`'s and `ClipControls`' highlight track | `ApexMarkersTests.cs` |
| `HighlightSegments.razor` | Lane-stacked highlight track drawn above `ScrubBar`'s scene bar | `HighlightSegmentsTests.cs` |
| `MultiSelectFilterGroup.razor` (+ `MultiSelectOption.cs`) | Checkbox filter group | — |
| `PosterCard.razor` | One poster tile (image, status badge, click-through) | `PosterCardTests.cs` |
| `PosterGrid.razor` | Layout grid wrapping `PosterCard` tiles | `PosterGridTests.cs` |
| `R18DevReleaseCard.razor` | An r18.dev release as a `PosterCard` with hover Prowlarr-search/add actions for releases not in the library (actor Missing page, Movies "All releases") | `R18DevReleaseCardTests.cs` |
| `ProwlarrSearchModal.razor` | Modal for searching/grabbing Prowlarr torrent results | — |
| `StatusFilterButtonGroup.razor` (+ `FilterButtonOption.cs`) | Toggle-button group for status filters (All/Missing/Got) | `StatusFilterButtonGroupTests.cs` |
| `PosterImageLoading.cs` | Enum: lazy vs. eager poster image loading strategy | — |

## Services (`Javbuddy/Services/`)

Domain services are grouped into `Services/<Group>/` folders whose namespace matches (`Javbuddy.Services.<Group>`); the file names in the table below sit in these groups:

| Group | Holds |
|---|---|
| `Movies/` | movie reads and mutations, add/rescan/stream/cover-crop, extrafanart add/delete (`MovieExtraFanartService`), the cleanup (Review) flow, deletion history recording/matching and review/purge, filters and code normalization, header search |
| `Actors/` | actor CRUD/merge/import, discovery, photos, name and physical-attribute helpers |
| `Scenes/` | scenes, highlights, chapter import/write, scene detection and time helpers, the scenes wall queries |
| `Nfo/` | .nfo sync, drift detection/push, history, the .nfo file writer and the pure XML helpers |
| `Tags/` | tag management, normalization, script detection, actor tags (`ActorTagService`, the filters' `ActorTagOptions`) |
| `Torrents/` | grab, the Activity Queue and History services, change notifier, path mapping, and the TorrentSort wizard (`TorrentSortService`, the thin `TorrentSortWizardState` coordinator, its per-step parts in `SortWizard/`, its helpers) |
| `Settings/` | connection settings save/read and connection status tiles (per-source discovery toggles live in `MovieDiscovery/DiscoverySettingsService`) |
| `Monitoring/` | system status, disk space, task activity and change notifiers |
| `Images/` | image serving, upload limits, remote image download, and the local WebP image caches (poster/fanart and actor images), converter and crop geometry |
| `Common/` | small cross-cutting helpers (`ByteSizeFormatter`, `StringExtensions`, `JustifiedGridLayout`) |

`Models/` is grouped into matching subfolders too (Movies, Actors, Tags, Scenes, Nfo, Torrents, Settings, Tasks, Images), but every entity keeps the namespace `Javbuddy.Models`: the EF migrations and model snapshot reference entities by that full name, so changing it would generate a spurious migration.

Domain services (`Services/<Group>/*.cs`):

| File | Responsibility |
|---|---|
| `ActorMatching.cs` | Match a discovered actor against already-tracked actors |
| `ActorPhotoService.cs` | Actor gallery albums/photos, storage lifecycle, and portrait promotion |
| `ByteSizeFormatter.cs` | Format byte counts for display |
| `CodeNormalization.cs` | Normalize JAV codes (`ABCD-123` ↔ `ABCD123`) |
| `ConnectionSettingsSaveService.cs` | Persist integration connection settings |
| `ImageServingService.cs` | Serve cached poster/fanart/actor images to the browser |
| `ImageUploadSettings.cs` | Strongly-typed operational image upload size configuration |
| `MediaInfoTextFormatter.cs` | Format probed media-file info for display |
| `VrFormat.cs` | A video's VR / 3D format: Jellyfin-style file-name tokens, the 2:1 and VR-genre fallbacks, poster badge text, version-tag stripping; applied on refresh by `LocalLibraryMetadataMapper.ApplyVrFormats`, set by hand via `MovieService.SetVrTypeAsync` (Edit Metadata's VR format field) |
| `MovieAddService.cs` | Add a new movie via metadata lookup and clear matching deletion history |
| `DeletedMovieHistory.cs` | Record deletion history in the caller's save, clear exact/canonical matches on re-add or library-rescan import, and query candidate history for r18.dev matching; tested by `Javbuddy.Tests/Services/Movies/DeletedMovieHistoryTests.cs` and the movie add/delete service tests |
| `DeletedMovieService.cs` | Page through deleted-movie history and purge individual records or all history; tested by `Javbuddy.Tests/Services/Movies/DeletedMovieServiceTests.cs` |
| `MovieChangeNotifier.cs` | Cross-circuit pub/sub so other open tabs' Movies grids refresh |
| `MovieActorAssociation.cs` | Synchronizes indexed tracked-actor links from a movie's metadata cast |
| `MovieRescanService.cs` | Unified on-demand single movie rescan (local files, .nfo, javinizer-go, actors, Jellyfin) |
| `MovieExtraFanartService.cs` | Adds (upload/URL, saved as the next `fanartN`) and deletes images in a movie's local `extrafanart` folder, purging the index-keyed extrafanart cache rows from the first changed position |
| `MovieApexService.cs` (+ `ApexRanges.cs`) | Per-movie apex CRUD with library tags, actors and favorites; `ApexRanges` holds time validation, the playback window (lead-in/tail) and clip shifting; `ApexPlaybackSettingsService` holds the default window (single-row settings, per-field `ApexPlayback__*` env overrides) | `MovieApexServiceTests.cs`, `ApexRangesTests.cs` |
| `MovieHighlightService.cs` (+ `HighlightRanges.cs`) | Per-movie highlight CRUD and favorite; `HighlightRanges` holds the pure validation and timeline-lane helpers |
| `ClipRollup.cs` / `ClipActors.cs` / `ClipTree.cs` | Pure rules: tags rolling up from apexes and highlights to the highlights and scenes around them, actors flowing down (own, else parent highlight, scene, cast), and the editor's tree; see [scenes-and-apexes.md](scenes-and-apexes.md) | `ClipRollupTests.cs`, `ClipActorsTests.cs`, `ClipTreeTests.cs` (over `ClipCases.cs`) |
| `ClipRollupQueries.cs` | EF-translatable mirror of `ClipRollup`, plus reads of the stored effective actors, for the Scenes wall's filters, options and cards | `ClipRollupQueriesTests.cs`, `ClipWallFilterTests.cs` |
| `ClipActorSync.cs` | Recomputes a stale movie's stored effective actors (`SceneEffectiveActor` and co.) from `ClipActors`; `ClipActorStale` marks movies stale where the tracker can't see | `ClipActorSyncTests.cs`, `ClipActorExplicitMarkTests.cs` |
| `ClipActorStaleInterceptor.cs` | `SaveChangesInterceptor` setting `Movie.ClipActorsStale` on tracked changes that alter effective actors, and `ClipActorRefreshSignal` to wake the worker | `ClipActorStaleInterceptorTests.cs` |
| `ClipActorRefreshWorker.cs` | Background service refreshing stale movies on startup (the first fill) and on signal | `ClipActorRefreshWorkerTests.cs` |
| `ClipTagSync.cs` | Keeps `MovieTag.FromClips` in step with the movie's clip tags; `ClipTagSyncService` adds the `MetaGenres` / `.nfo` follow-up | `ClipTagSyncTests.cs`, `ClipTagMaintenanceTests.cs`, `MovieTagFlagsTests.cs` |
| `ClipActorTags.cs` | Pure rules for actor tags (`Tag.IsActorTag`): nearest-level-wins inheritance down and roll-up up, per actor. Stored by `ClipActorSync` as `SceneEffectiveActorTag` and siblings; see [scenes-and-apexes.md](scenes-and-apexes.md) | `ClipActorTagsTests.cs`, `ClipActorTagSyncTests.cs`, `ClipTagSyncRaceTests.cs`, `ActorTagFilterTests.cs` |
| `MovieService.cs` | Movie mutations: status toggle, delete, Jellyfin link |
| `MovieGridQueryService.cs` | Movies grid reads: combined filter/sort query (incl. the actor-name filter), status counts, filter options, scroll windows, downloading codes; the DeoVR lists' Got-with-a-file query |
| `MovieCleanupService.cs` |'s Cleanup review flow: eligible-movie queue (ordering/filtering), cast resolution + fresh `MetaActresses` read for in-card cast edits, tag list for in-card tag edits, delete-with-folder, 90-day snooze, permanent blacklist (+ reversal) |
| `RemoteImageDownloader.cs` | Bounded "download image from URL" transport shared by the actor portrait, movie cover and movie extrafanart flows; `RemoteImageDownloadMessages.cs` holds the shared failure wording |
| `PublicAddressGuard.cs` | Connection callback for the `UserImageImport` HTTP client: those URL imports refuse loopback/private/link-local addresses, also after DNS and redirects |
| `SearchService.cs` | Header search box: ranked substring match across movies/actors |
| `TorrentChangeNotifier.cs` | Cross-circuit pub/sub for torrent queue changes |
| `TorrentGrabService.cs` | Send a torrent to qBittorrent |
| `TorrentQueueService.cs` | Activity > Queue reads (active downloads, count for the sidebar badge) and removal of selected downloads from qBittorrent |
| `SystemStatusService.cs` | System > Status: migration state, health messages, which integrations are configured (via `ConnectionStatusService`) and their live connection tests |
| `MovieDetailQueryService.cs` / `ActorDetailQueryService.cs` | Movie Detail's movie/tag/cast reads and Actor Detail's movie/photo-count reads, so those pages don't open a DbContext |
| `StringExtensions.cs` | `TrimToNull` (trim, or null when blank) |
| `NfoActorMatching.cs` / `NfoFieldSync.cs` / `NfoXmlFormatter.cs` | Pure static .nfo XML logic used by `NfoSyncService`: actor name matching/consolidation, writing canonical fields, indented serialization |

`Infrastructure/` — generic, domain-agnostic base classes; see `docs/architecture.md`'s "Service layering" for what each one does and why.

Per-integration subfolders (`Services/<Integration>/`):

| Folder | Files | Notes |
|---|---|---|
| `Javinizer/` | `JavinizerClient`, `JavinizerDtos`, `MovieMetadataMapper`, `MovieMetadataImportOptions` | Scraper client and the mapping of its results onto movies; the TorrentSort wizard and `PathMappingService` that use it live in `Torrents/` |
| `Jellyfin/` | `JellyfinClient`, `JellyfinDtos`, `JellyfinMetadataMapper` | Media server integration |
| `Prowlarr/` | `ProwlarrClient`, `ProwlarrDtos` | Torrent indexer integration |
| `QBittorrent/` | `QBittorrentClient`, `QBittorrentDtos` | Torrent client integration |
| `LocalLibrary/` | `LocalLibraryClient`, `LocalNfoParser`, `LocalLibraryDtos`, `LocalLibraryMetadataMapper` | Local filesystem scanning and the movie .nfo parser; the image cache built on it lives in `Images/`, the .nfo writer in `Nfo/` |
| `MediaInfo/` | `MediaInfoProber` | Wraps the native `libmediainfo` library for video file introspection |
| `R18Dev/` | `R18DevDumpStore`, `R18DevDumpImporter`, `DumpParser`, `R18DevDumpPaths`, `R18DevCatalogFacets` (per-release columns + indexes the catalog browse filters on, built at import), `R18DevReleaseMatcher` (Got/Wanted/Missing vs the library), `R18DevReleaseBrowseService` (catalog browse, per-status counts, related releases, code suggestions), `ActorFilmographyService` (actor Missing page filmography + r18.dev name override) | Reads/imports the read-only r18.dev SQL dump (the one place raw SQL is expected — see `AGENTS.md`) |
| `Warashi/` | `WarashiClient`, `WarashiHtmlParser`, `WarashiModels`, `IWarashiClient`, `WarashiSettingsService` | WAPdB (warashi-asian-pornstars.fr) client, microdata scraper, DTOs, and the settings single-row save service |
| `Ffmpeg/` | `FfmpegClient`, `FfmpegBinaryResolver`, `FfmpegModels` | Wraps `ffmpeg` for video processing (used by `VrMerge/`) |
| `VrMerge/` | `VrMergeService`, `VrPartDetector`, `VrMergeJobTracker`, `VrMergeModels` | VR multi-part file merge workflow |

`Startup/` (outside `Services/`) — `ServiceRegistration` holds Program.cs's DI registrations as `Add*` extension methods (order matters for multi-registrations such as `IActorMetadataSource` and `IScheduledTask`), `MediaEndpoints.MapMediaEndpoints` the image/stream/trickplay/scene-media endpoints, and `DeoVrEndpoints.MapDeoVrEndpoints` the DeoVR JSON API.

`Tasks/ScheduledTaskOverviewService.cs` is System > Tasks' read model (per-task last run and next due, recent history, the active run for the sidebar, a task's latest/last-completed run). Single-row settings are read with `ReadSingleRowAsync` (`Data/SingleRowSettingsQueryExtensions.cs`, untracked) or through `SingleRowSettingsRepository.GetAsync`; `R18DevSettingsService` and `MediaInfoSettingsService` are the per-source settings services for those two tiles.

`SceneMedia/` — `SceneMediaService` (scene screenshots/previews), `HighlightMediaService` (highlight ones), both over `ClipMediaGenerator` (ffmpeg → WebP image-cache rows stamped with the sampled range) and one background `SceneMediaQueue` worker (one ffmpeg media job at a time). Served at `/scene-image/{id}/{variant}` and `/highlight-image/{id}/{variant}`. The Scenes wall's queries, including the highlight view, are `SceneWallQueryService.cs`.

`DeoVr/` — DeoVR's legacy JSON API: `DeoVrService` (the `/deovr` list — one per group, in order — and one movie's document, every version an encoding, scenes as chapters), `DeoVrGroupService` (groups: a `MovieGridFilter` stored as JSON + sort, ordered; a seeded, deletable "All movies"; only Got movies with a file are listed, via `MovieGridQueryService.GetWithFilesAsync`), `DeoVrSettingsService` (single-row settings, `DeoVr__Enabled` env override), `DeoVrProjection` (screen type/stereo mode from the primary version's `VrFormat`; the file name only picks a fisheye lens), `DeoVrUrls`, `DeoVrDtos`. Routes in `Startup/DeoVrEndpoints.cs` (`/deovr`, `/deovr/movies/{id}`, `/deovr/stream/{movieId}/{fileId}/{fileName}`, `/deovr/thumb/{id}`), all 404 while disabled.

`MovieStreamService.cs` — resolves a movie's main local video file (`MovieVideoFiles`) inside a library root for the players, served with byte ranges at `/api/movies/{id}/stream`; Jellyfin isn't involved in playback.

`Trickplay/` — `TrickplayService` (a movie's scrub-bar trickplay: its locally generated set, else Jellyfin's when the fallback setting allows), `TrickplayStore` (the object store's `trickplay/<CODE>/<identity>/N.webp` sheets; `TrickplaySet` rows index them and are written last as the commit marker,; keyed by `TrickplayIdentity` = file name + duration + resolution), `TrickplayGenerator` (ffmpeg → a set), `TrickplayQueue` (one background worker, one movie at a time, progress in the sidebar via `TaskActivityTracker`), `TrickplayTrigger` (called by `LocalLibraryClient.RefreshMediaInfoOnlyAsync` when a video file changed), `TrickplayMainFiles` (the main file the way `MovieVideoFiles` picks it), `TrickplaySettingsService` (single-row settings, per-field `Trickplay__*` env overrides), `TrickplayLayout` + `TrickplayTiles` (the provider-neutral layout the ScrubBar/ClipEditor draw from). Local tiles are served at `/trickplay/{movieId}/{identity}/{index}.webp`. `HighlightTrickplayService` + `HighlightTrickplay` (a highlight's own denser set for the clip player, keyed by its range and the file's identity, generated on the `SceneMediaQueue`,; queued by `HighlightMediaService` and the backfill, never by playing). `TrickplayGenerationTracker` (which sets are generating right now and their percent, reported by `TrickplayGenerator`; the players show it in the scrub-bar preview via `TrickplayGeneratingPreview` and reload the set when it ends).

`Tasks/` — `ScheduledTaskHostedService` + `ScheduledTaskRunner` run the background scheduler (System > Tasks); `IScheduledTask` implementations: `ImageCacheTask`, `JellyfinLinkSyncTask`, `QBittorrentSyncTask`, `LibraryRescanTask`, `R18DevImportTask`, `TrickplayBackfillTask` (only queues; the scheduler runs tasks one after another, so long work goes to a background queue).

Unit tests for all of the above mirror this structure 1:1 under `Javbuddy.Tests/Services/` (namespace `Javbuddy.Tests.Services.<Folder>`) (e.g. `Services/Ffmpeg/FfmpegClient.cs` → `Javbuddy.Tests/Services/Ffmpeg/FfmpegClientTests.cs`) — a service file with no matching test file has no unit test coverage.

## Test project structure

`Javbuddy.Tests/` (xUnit + bUnit + NSubstitute) mirrors the source tree:
- `Components/Pages/`, `Components/Layout/`, `Components/Shared/` — bUnit render tests, one `*Tests.cs` per tested component (not every component has one — see tables above).
- `Services/` (+ per-integration subfolders matching `Services/<Integration>/`) — unit tests for service/mapper/helper logic.
- `Data/AppDbContextTests.cs` — EF Core/SQLite schema tests.
- `TestSupport/` — `TestDbContextFactory` (in-memory SQLite), `FakeHttpClientFactory`/`FakeHttpMessageHandler` (HTTP client fakes), `ControlledStream` (chunked/blocking/failing response bodies), `StreamServiceStubs` (an `IMovieStreamService` where every movie is playable, for tests that render a player).

`Javbuddy.E2ETests/` (xUnit + Playwright, real Kestrel) is organized by test *kind*, not by mirroring `Services/`/`Components/`:
- `SmokeTests/` — one `<Page>PageTests.cs` per page, asserting it renders. Naming ties directly back to the page: `MoviesPageTests.cs` ↔ `Movies.razor`.
- `Flows/` — multi-step interaction tests spanning one or more pages (e.g. `AddMovieFlowTests.cs`, `SettingsConnectionsFlowTests.cs`).
- `Fixtures/` — `E2EFixture`/`JavbuddyAppFactory` (shared real app+browser instance), `FakeServices/` (in-process fake `Javinizer`/`Jellyfin`/`Prowlarr`/`QBittorrent` servers).
- `Support/DbSeeding.cs` — seed data for E2E runs.

A page's full test coverage = its bUnit row above + its `SmokeTests`/`Flows` entries here.
