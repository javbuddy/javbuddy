# Changelog

All notable changes to this project are documented in this file, in [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format. This project adheres to [Semantic Versioning](https://semver.org/) (pre-1.0: breaking changes may land in a minor bump).

## [Unreleased]

### Changed

- Docker image: MediaInfo now comes from the `MediaInfo.Wrapper.Core` NuGet package (bumped to 26.10.0, which ships generic Linux runtime identifiers) instead of the distro's `libmediainfo0v5`; only its `libcurl3-gnutls` and `libmms0` dependencies are installed from apt.

### Fixed

- Movie Detail's Delete dialog no longer offers "Delete files from disk" for a movie with no files on disk, such as a wanted movie.

### Added

- Getting started: a "Folder permissions" section covering the non-root container user, checking and fixing mount ownership, and common access errors.

### Added

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
