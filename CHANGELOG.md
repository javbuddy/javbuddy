# Changelog

All notable changes to this project are documented in this file, in [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format. This project adheres to [Semantic Versioning](https://semver.org/) (pre-1.0: breaking changes may land in a minor bump).

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
