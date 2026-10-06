# Library Guide

How Javbuddy expects your collection to be organized, how to bring an existing one in, how finished downloads get sorted into it, and what to do when your services see different paths. For the full on-disk format (`.nfo` dialects, `.actors/`, subtitle and VR naming) see [local-library-format.md](local-library-format.md). For every setting, see [configuration.md](configuration.md).

## Folder layout

Javbuddy reads your library straight from disk. You give it one or more **root folders** (`LocalLibrary__RootPaths__0`, `__1`, …, or **Settings → Metadata**), and:

- **Each immediate subfolder of a root is one movie**, named after its release code.
- Folders deeper than that are not scanned as movies.

```
/media/jav/            ← a root
├── ABCD-123/          ← one movie, named by code
│   ├── ABCD-123.mp4
│   ├── ABCD-123.nfo
│   ├── poster.jpg
│   └── fanart.jpg
└── EFGH-456/
    └── EFGH-456.mkv
```

You can use several roots to separate your collection, e.g. `/media/jav` and `/media/vr`.

Everything beside the video is optional. A `.nfo`, `poster.*`, `fanart.*`, `extrafanart/`, a `-trailer` video, subtitles and a `.actors/` folder are picked up when present. If you use javinizer-go to organize, it writes these for you. See [local-library-format.md](local-library-format.md) for details.

**Tip:** if you let javinizer-go organize your files, set its folder format to the bare code (`folder_format: <ID>`), otherwise its default names such as `ABCD-123 [Studio] - Title (2024)` won't match a code.

## Importing an existing collection

1. Add your library folder as a root, either with `LocalLibrary__RootPaths__0` or under **Settings → Metadata**. Nothing is scanned until at least one root is set.
2. Open **Movies → Library Import** (`/add/import`) and press **Run Library Rescan**.

Every folder found is added as **Got**, with its `.nfo`, poster and fanart read from disk. Movies already tracked are skipped, nothing you already track is changed. It's the same task as **System → Tasks → Library Rescan**, so it also reads media details (resolution, codec, duration), subtitles and trailers for the whole library. A large library takes a while the first time. Progress is shown on the button.

A scheduled rescan keeps picking up folders you add later, so after the first import you rarely need to run it by hand.

If a movie lacks metadata (no `.nfo`), open it and fetch it through javinizer-go, or use **Edit Metadata**.

## Sorting a finished download

**Torrent sort** turns a finished download into a library folder. Open **Activity**, find the download and press **Sort**. The wizard steps are:

1. **Merge** (VR only): joins multi-part VR videos into one file before sorting.
2. **Scan**: asks javinizer-go to scan the download folder and identify the movie codes in it. The path is pre-filled from qBittorrent's content path, as *javinizer-go* sees it. Fix it here if the scan comes back empty.
3. **Destination**: pick one of your destination aliases, i.e. the library folder to move into.
4. **Scrape**: javinizer-go fetches the metadata for each match.
5. **Review**: check the matches, posters and screenshots, and fix wrong ones.
6. **Preview**: shows the final folder and file names before anything moves.
7. **Organize**: javinizer-go renames and moves the files into the destination, and the movie becomes **Got**. You can then remove the download from qBittorrent.

Destination aliases are set with environment variables on Javbuddy, one `Name`/`Path` pair per destination:

```yaml
environment:
  Javinizer__DestinationAliases__0__Name: jav
  Javinizer__DestinationAliases__0__Path: /media/jav
  Javinizer__DestinationAliases__1__Name: vr
  Javinizer__DestinationAliases__1__Path: /media/vr
```

The `Path` is the folder **as javinizer-go sees it**, and it should be one of the library roots Javbuddy scans (as *Javbuddy* sees it), so the sorted movie shows up in your library. With the same mount paths in every container, those two are the same string.

## When the services see different paths

Javbuddy, javinizer-go and qBittorrent each talk about files by the paths in *their own* container. If a download is `/downloads/complete/ABCD-123` to qBittorrent but `/data/torrents/ABCD-123` to javinizer-go, passing the first path along fails.

The easiest fix is avoiding the problem: mount the same host folders at the same path in every container (see [Getting Started](getting-started.md#1-plan-your-folders)).

Otherwise, add a **path mapping** under **Settings → Connections → Path Mappings**. One mapping describes one folder as all three see it:

| Field | Meaning | Example |
|---|---|---|
| qBittorrent prefix | The folder as qBittorrent reports it | `/downloads/complete` |
| javinizer-go prefix | The same folder as javinizer-go sees it | `/data/torrents` |
| Javbuddy prefix (optional) | The same folder as Javbuddy sees it. Needed for VR part merging, and for matching the destination to your library | `/mnt/torrents` |

Javbuddy rewrites paths between them using the longest matching prefix. A path that matches no mapping is used unchanged. You can add several mappings, for example one for downloads and one for the library.

With environment variables you can set a single mapping (`PathMapping__QBittorrentPrefix`, `PathMapping__JavinizerPrefix`, `PathMapping__AppPrefix`). When set, it overrides the ones in the UI.

If Torrent sort's **Scan** step comes back empty, the path is the first thing to check: edit it to the folder as javinizer-go sees it, then add a mapping so future downloads pre-fill correctly.
