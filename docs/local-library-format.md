# Local Library Format

Local directories are the metadata source: the app scans configured root paths (`LocalLibrary:RootPaths`) directly, rather than gathering metadata through Jellyfin's API (an earlier approach that was deliberately removed — see `docs/jellyfin-integration.md` for what Jellyfin is still used for instead). This doc is the reference for that on-disk format; the real parsing lives in `Services/LocalLibrary/LocalLibraryClient.cs`, which this doc is kept in sync with.

## Root paths

Configured via `LocalLibrary:RootPaths`, one path per JAV "channel" the user wants scanned, e.g.:

- `/media/jav`
- `/media/vr`

## Folder layout

```
/media/vr/ABCD-123
│   fanart.jpg
│   poster.jpg
│   ABCD-123.mp4
│   ABCD-123-trailer.mp4
│   ABCD-123.nfo
│   ABCD-123.transcribed.en.srt
│
├───.actors
│       Jane Example.jpg
│
└───extrafanart
        fanart1.jpg
        fanart2.jpg
        fanart3.jpg
```

`poster`/`folder`/`fanart` are also matched with `.jpeg`, `.png`, or `.webp` instead of `.jpg` (checked in that extension order); `poster.*` is preferred over `folder.*` when both exist.

## Actor images (`.actors/`)

A movie folder can have its own `.actors` subfolder holding cast headshots, named after the actor — `ResolveActorImagePathAsync` (`LocalLibraryClient.cs`) looks there. Matching is an **exact, case-insensitive filename match against the actor's `Name` in Javbuddy's own database** (e.g. `.actors/Jane Example.jpg` only resolves for an actor whose `Name` is literally `Jane Example`) — not fuzzy, and not tried against any alternate spelling.

Example of a messy `.actors` folder:

```
Mei.jpg
Doe_Jane.jpg
Jane_Doe.jpg
Roe_Mary.jpg
Mary_Roe.jpg
Thumbs.db
```


This shows what that exact-match rule looks like in practice on a messy library:

- The same two actresses each have **two filename variants with the name order swapped and a space replaced by an underscore** (`Doe_Jane.jpg` / `Jane_Doe.jpg`, `Roe_Mary.jpg` / `Mary_Roe.jpg`) — whatever tool populated this folder hedged against not knowing which order/spelling Javbuddy's `Actor.Name` would end up in. At most one of each pair will ever actually match; the other sits there unused.
- `Mei.jpg` is a single stage name with no surname — matching still works the same way, exact-match against whatever `Actor.Name` is.
- `Thumbs.db` (a Windows Explorer thumbnail cache file, not an actor photo) is harmless here — only `.jpg`/`.jpeg`/`.png`/`.webp` files are even considered, so it's silently skipped.

`ActorImageCacheService` is what actually calls this: for a given actor, it searches every movie already linked to them in the DB (via `Movie.MetaActresses`, newest movie first — **not** a library-wide folder scan) and caches the first `.actors/` match it finds as WebP (thumb + full), keyed by source file size/mtime so a changed or added image gets picked up on the next request rather than serving a stale cache forever.

## Video, trailer, and subtitle files

- **Movie video**: recognized extensions are `.mp4`, `.mkv`, `.avi`, `.m4v`, `.wmv`, `.mov`, `.ts`, `.webm`. The file named exactly `{code}.{ext}` is preferred; if that's not found, the first remaining video file in the folder is used instead — but see the trailer exclusion below.
- **VR / 3D format**: read from each video file's name, never written to it. Tokens are split on Jellyfin's flag delimiters (`( ) - . _ [ ]` and spaces) after the movie's code, so a code's own number (`ABCD-180`) doesn't count. Jellyfin's 3D layouts (`3d.hsbs`, `hsbs`, `sbs`, `fsbs`, `htab`, `tab`, `ftab`, `sbs3d`, `mvc`, plus `ou`/`hou`) give `3D HSBS`, `3D SBS`, …; a VR projection (`180`/`VR180`, `360`/`VR360`, `fisheye`/`fisheye190`) wins over them, with `SBS` or `TB` when a layout is also tagged (`sbs`, `lr`, `tb`, `tab`, …; `WXYZ-059_180_sbs.mp4` → `VR180 SBS`, `WXYZ-059_fisheye_lr.mp4` → `Fisheye SBS`); a bare `VR` token gives `VR`. A bare `180` or `fisheye` tag on a 2:1 frame is `VR180 SBS` / `Fisheye SBS`. With no tag, a 2:1 (side-by-side) frame is `VR180 SBS`, else a `VR` genre gives `VR`. The format's tokens are left out of the version tag (`WXYZ-059-4K.3d.htab.mp4` is version `4K`, format `3D HTAB`). A format set by hand in Edit Metadata is kept across refreshes.
- **Trailer**: a video file whose name ends in `-trailer` (e.g. `ABCD-123-trailer.mp4` sitting alongside the real `ABCD-123.webm`) is recognized as a trailer, not a candidate for the movie's own video file — `FindVideoFile` explicitly excludes it, and `HasTrailerFile` looks for exactly this suffix. This only sets a `HasTrailerFile` flag on the movie; the trailer file itself isn't otherwise processed.
- **Subtitles**: any file with a `.srt`, `.ass`, `.ssa`, `.vtt`, or `.sub` extension anywhere in the folder counts as a subtitle sidecar and sets `HasSubtitleFile`. In practice every subtitle file seen in the real library is `.srt`, and it's rarely named `{code}.srt` — usually it's named after whatever transcription tool produced it (e.g. `ABCD-123.whisper large v3.en.srt`, the naming pattern for Whisper-generated subtitles). The other extensions are supported defensively, not because they've been seen in practice.
- Both `HasTrailerFile` and `HasSubtitleFile` are recomputed independently of the movie's own video file resolving successfully — a subtitle or trailer can be dropped into an already-scanned movie's folder at any time and gets picked up by a targeted refresh (`RefreshSubtitleFlagOnlyAsync`/`RefreshTrailerFlagOnlyAsync`) without a full rescan.

## `.nfo` metadata

The `.nfo` file contains the metadata and needs to be parsed. Two dialects show up in the wild — the library has both, and `ParseNfoAsync` (`Services/LocalLibrary/LocalLibraryClient.cs`) handles either one, field by field, rather than assuming one fixed shape:

- **javinizer-go** (current scraper) — richer: `<sorttitle>`, `<uniqueid>`, a `<ratings><rating><value>` wrapper, `<thumb aspect="poster">`/`<fanart><thumb>` artwork URLs, a nested `<set><name>`.
- **javinizer** (the older, classic scraper — example below) — flatter: a bare `<rating>`/`<votes>` pair instead of the `<ratings>` wrapper, no `<sorttitle>`/`<uniqueid>`, usually no artwork `<thumb>` at all (poster/fanart come from `poster.jpg`/`fanart.jpg` on disk instead), a `<set>` that's flat text rather than nested, plus a few fields javinizer-go doesn't emit (`<mpaa>`, `<tag>`, `<role>` on `<actor>`) that are simply ignored.

`ParseNfoAsync` copes with both by reading whichever shape is present rather than picking a dialect up front: rating from `<ratings>/<rating>/<value>` if present, else the flat `<rating>`; poster from a `<thumb aspect="poster">` if present, else a bare `<thumb>` with no `aspect` attribute; series name from `<set>/<name>` if present, else `<set>`'s own text; release date from `<releasedate>`, falling back to `<premiered>`, falling back to just `<year>` (January 1st) if neither parses.

Example `ABCD-123.nfo` (javinizer-go dialect; URLs should only be used as a fallback — prefer the local files):

```xml
<?xml version="1.0" encoding="UTF-8"?>
<movie>
  <title>ABCD-123 An Example Title</title>
  <originaltitle>サンプルタイトル</originaltitle>
  <sorttitle>ABCD-123</sorttitle>
  <id>ABCD-123</id>
  <uniqueid type="contentid" default="true">ABCD-123</uniqueid>
  <plot>An example plot.</plot>
  <runtime>69</runtime>
  <year>2026</year>
  <releasedate>2026-01-15</releasedate>
  <premiered>2026-01-15</premiered>
  <ratings>
    <rating name="r18dev" max="10" default="true">
      <value>8</value>
    </rating>
  </ratings>
  <director>Example Director</director>
  <actor>
    <name>Jane Example</name>
    <thumb>https://example.com/actors/jane_example.jpg</thumb>
  </actor>
  <studio>Example Studio</studio>
  <maker>Example Studio</maker>
  <label>Example Label</label>
  <set>
    <name>Example Series</name>
  </set>
  <genre>Solowork</genre>
  <genre>VR</genre>
  <thumb aspect="poster">https://example.com/images/poster.jpg</thumb>
  <fanart>
    <thumb>https://example.com/images/fanart-1.jpg</thumb>
    <thumb>https://example.com/images/fanart-2.jpg</thumb>
  </fanart>
  <trailer>https://example.com/trailers/abcd-123.mp4</trailer>
</movie>
```

Note the `<trailer>` XML field above is a metadata URL from the scraper, unrelated to the `{code}-trailer.{ext}` local trailer *file* convention described above — the app doesn't currently download/use this URL, only the local sidecar file's presence is tracked.

Example `EFGH-456.nfo` (classic javinizer dialect) — note the flat `<rating>`/`<votes>` instead of a `<ratings>` wrapper, no `<sorttitle>`/`<uniqueid>`, no artwork `<thumb>` at all, and the `<mpaa>`/`<tag>`/`<actor><role>` fields javinizer-go doesn't emit:

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<movie>
    <title>EFGH-456 Another Example Title</title>
    <originaltitle></originaltitle>
    <id>EFGH-456</id>
    <releasedate>2017-02-01</releasedate>
    <year>2017</year>
    <director>Example Director</director>
    <studio>Example Studio</studio>
    <rating></rating>
    <votes></votes>
    <plot></plot>
    <runtime>180</runtime>
    <trailer></trailer>
    <mpaa>XXX</mpaa>
    <set></set>
    <tag></tag>
    <genre>Solowork</genre>
    <genre>Schoolgirl</genre>
    <actor>
        <name>Mary Example</name>
        <thumb></thumb>
        <role>Actress</role>
    </actor>
</movie>
```

Many fields are present-but-empty here (`<rating>`, `<votes>`, `<plot>`, `<originaltitle>`, `<set>`, `<tag>`, the actor's `<thumb>`) — `ParseNfoAsync`'s `El()` helper treats a whitespace-only element the same as a missing one, so these correctly come through as `null`/unset rather than empty strings.
