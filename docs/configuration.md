# Configuration Reference

Javbuddy uses standard .NET configuration (`IConfiguration`), supporting both `appsettings.json` and environment variables. In containerized or production deployments, configuring via **environment variables** is the recommended approach.

## Precedence & Behavior

1. **Environment Variables**: Highest priority. Overrides values from `appsettings.json`.
2. **`appsettings.json`**: Baseline defaults for deployment-level settings (database connection, logging, static asset reloading).
3. **Database (UI Settings)**: For integrations that support runtime UI configuration (e.g. Javinizer, Jellyfin, Prowlarr, qBittorrent, Path Mappings), database-stored values are used when the corresponding environment variables are not set. When environment variables are set, they lock the configuration and override the UI.

> [!WARNING]
> In ASP.NET Core, declaring an optional configuration key as an empty string (e.g. `"Path": ""`) in `appsettings.json` assigns an empty string value rather than leaving it unset (`null`). Optional keys should be omitted from `appsettings.json` so the application can apply its built-in defaults.

## Environment Variable Syntax

In Linux, Docker, and shell environments, nested JSON configuration keys use double underscores (`__`) as delimiters:

| Configuration Key | Environment Variable |
|---|---|
| `Database:Provider` | `Database__Provider` |
| `Javinizer:BaseUrl` | `Javinizer__BaseUrl` |
| `LocalLibrary:RootPaths:0` | `LocalLibrary__RootPaths__0` |
| `ImageCache:Mode` | `ImageCache__Mode` |
| `ImageUpload:MaxSizeMb` | `ImageUpload__MaxSizeMb` |
| `ImageUpload:MaxBatchFiles` | `ImageUpload__MaxBatchFiles` |
| `ObjectStore:Path` | `ObjectStore__Path` |

---

## Configuration Keys

### Database & Storage

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `Database:Provider` | `Database__Provider` | `Sqlite` | Database provider (`Sqlite`). |
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | `Data Source=Javbuddy.db` | SQLite connection string or database file path. |
| `DataProtection:KeysPath` | `DataProtection__KeysPath` | *(system default)* | Directory for ASP.NET Data Protection XML keyring (persists auth cookies across container restarts). |

### Integrations

#### Javinizer (Metadata Scraper)

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `Javinizer:BaseUrl` | `Javinizer__BaseUrl` | *(none)* | Base URL for internal communication with the `javinizer-go` backend (e.g. `http://javinizer:8080`). |
| `Javinizer:ExternalUrl` | `Javinizer__ExternalUrl` | *(none)* | Optional browser-facing URL if different from `BaseUrl`. |
| `Javinizer:ApiToken` | `Javinizer__ApiToken` | *(none)* | API token for `javinizer-go` authentication. |
| `Javinizer:DestinationAliases` | `Javinizer__DestinationAliases__0__Name`, `Javinizer__DestinationAliases__0__Path` | `[]` | Named destinations offered in Torrent sort, one `Name`/`Path` pair per index (`__1__Name`, …). `Path` is the folder as javinizer-go sees it. |

#### Prowlarr (Torrent Indexer)

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `Prowlarr:BaseUrl` | `Prowlarr__BaseUrl` | *(none)* | Base URL of the Prowlarr instance (e.g. `http://prowlarr:9696`). |
| `Prowlarr:ExternalUrl` | `Prowlarr__ExternalUrl` | *(none)* | Optional browser-facing URL if different from `BaseUrl`. |
| `Prowlarr:ApiKey` | `Prowlarr__ApiKey` | *(none)* | API key for Prowlarr access. |

#### Jellyfin (Media Server)

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `Jellyfin:Enabled` | `Jellyfin__Enabled` | `true` | Enables or disables Jellyfin integration features. |
| `Jellyfin:BaseUrl` | `Jellyfin__BaseUrl` | *(none)* | Base URL of the Jellyfin instance (e.g. `http://jellyfin:8096`). |
| `Jellyfin:ExternalUrl` | `Jellyfin__ExternalUrl` | *(none)* | Optional browser-facing URL if different from `BaseUrl`. |
| `Jellyfin:ApiKey` | `Jellyfin__ApiKey` | *(none)* | API key created in Jellyfin Dashboard -> Advanced -> API Keys. |
| `Jellyfin:SelectedLibraryNames` | `Jellyfin__SelectedLibraryNames__0` | `[]` | Array of Jellyfin library names tracked for movie ownership matching. |
| `Jellyfin:LinkSyncIntervalHours` | `Jellyfin__LinkSyncIntervalHours` | `24` | Background interval in hours for resynchronizing movie ownership links. |

#### qBittorrent (Download Client)

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `QBittorrent:BaseUrl` | `QBittorrent__BaseUrl` | *(none)* | Base URL of qBittorrent Web UI (e.g. `http://qbittorrent:8080`). |
| `QBittorrent:ExternalUrl` | `QBittorrent__ExternalUrl` | *(none)* | Optional browser-facing URL if different from `BaseUrl`. |
| `QBittorrent:Username` | `QBittorrent__Username` | *(none)* | Username for qBittorrent Web UI authentication. |
| `QBittorrent:Password` | `QBittorrent__Password` | *(none)* | Password for qBittorrent Web UI authentication. |
| `QBittorrent:Category` | `QBittorrent__Category` | *(none)* | Torrent category monitored and assigned by Javbuddy (e.g. `jav`). |
| `QBittorrent:SyncIntervalSeconds`| `QBittorrent__SyncIntervalSeconds` | `30` | Background poll interval in seconds for downloading torrent status. |

### Local Library & Filesystem

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `LocalLibrary:RootPaths` | `LocalLibrary__RootPaths__0` | `[]` | Array of filesystem paths containing existing organized JAV movie libraries. |

### DeoVR

It overrides the **Enable DeoVR** setting of **Settings > DeoVR**, which then shows it read-only. The links in DeoVR's JSON always use the address DeoVR's request came in on (with the scheme from a reverse proxy's `X-Forwarded-Proto` header when it sends one), so open `/deovr` over HTTPS for DeoVR on Android.

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `DeoVr:Enabled` | `DeoVr__Enabled` | `false` | Serves the `/deovr` JSON API for the DeoVR player. Off, every `/deovr` route answers 404. |

### Path Mapping

Used when services run in different Docker containers or across different network mounts with differing directory paths:

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `PathMapping:QBittorrentPrefix` | `PathMapping__QBittorrentPrefix` | *(none)* | Download path prefix as reported by qBittorrent (e.g. `/downloads/complete`). |
| `PathMapping:JavinizerPrefix` | `PathMapping__JavinizerPrefix` | *(none)* | Corresponding path prefix as seen by `javinizer-go` (e.g. `/data/torrents`). |
| `PathMapping:AppPrefix` | `PathMapping__AppPrefix` | *(none)* | Corresponding path prefix as seen by Javbuddy (needed for VR part-merging). |

### FFmpeg (Video Processing & VR Merge)

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `Ffmpeg:Path` | `Ffmpeg__Path` | *(system PATH)* | Explicit path to `ffmpeg` executable. When unset, resolves `ffmpeg` from system `PATH`. |
| `Ffmpeg:ProbePath` | `Ffmpeg__ProbePath` | *(system PATH)* | Explicit path to `ffprobe` executable. When unset, resolves `ffprobe` from system `PATH`. |

### Image Cache

Javbuddy converts and caches cover posters, fanart, extrafanart, and actor images to WebP format for fast browsing. DeoVR's seek-bar preview mosaics are cached here too, as JPEGs, in every mode except `Disabled`, which builds them on each request instead. The TTL, size limit and purge apply to them like any cached image.

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `ImageCache:Path` | `ImageCache__Path` | `<ContentRoot>/cache` | Filesystem directory for sharded on-disk WebP cache files. |
| `ImageCache:Mode` | `ImageCache__Mode` | `LocalMovies` | Caching policy. Options: `Disabled`, `Thumbnails`, `LocalMovies`, `AllMovies`. |
| `ImageCache:TtlHours` | `ImageCache__TtlHours` | *(none / unlimited)* | Number of hours before cached images are deemed stale and re-fetched/re-generated. |
| `ImageCache:MaxSizeMb` | `ImageCache__MaxSizeMb` | *(none / unlimited)* | Maximum cache directory size in MB before evicting oldest cached files. |
| `ImageCache:QualityFull` | `ImageCache__QualityFull` | `82` | WebP conversion quality for full-size images (1-100). |
| `ImageCache:QualityThumb` | `ImageCache__QualityThumb` | `75` | WebP conversion quality for thumbnails (1-100). |

### Image Upload

Controls the maximum permitted file size and batch file count when uploading actor gallery photos or custom actor portraits, including URL imports.

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `ImageUpload:MaxSizeMb` | `ImageUpload__MaxSizeMb` | `20` | Maximum image upload file size in megabytes. |
| `ImageUpload:MaxBatchFiles` | `ImageUpload__MaxBatchFiles` | `500` | Maximum number of images allowed per actor photo upload batch. |

### Storage Tiers

Javbuddy's persisted data splits into three independent tiers with different I/O, durability, and cost requirements, so each can be backed by a different volume/storage class:

1. **Database & keys** (`ConnectionStrings:Default`, `DataProtection:KeysPath`) — the SQLite DB and the ASP.NET DataProtection key ring. Needs strong POSIX locking and low-latency random I/O; belongs on replicated block storage (Ceph RBD, Longhorn), never NFS — NFS risks corrupted SQLite locks and slow transactions.
2. **Disposable image cache** (`ImageCache:Path`) — generated WebP thumbnails/covers. High-IOPS, fully regenerable; fine on a single-replica PVC or fast local disk (NVMe/SSD), and safe to lose.
3. **Durable object store** (`ObjectStore:Path`) — canonical uploaded actor portraits and gallery photos, and generated trickplay. Bulk capacity rather than latency-sensitive; suits NFS/ReadWriteMany without consuming expensive replicated block storage. One store (`Services/Infrastructure/ObjectStore.cs`, currently backed by the filesystem) with one key prefix per kind of data.

### Application Data

All durable files live in one object store under `ObjectStore:Path`, one key prefix per kind of data:

- `actor-images/<2 hex>/<guid>.<ext>` — newly uploaded or downloaded actor portraits and gallery photos keep their original bytes here, outside the disposable WebP cache, with their original `.jpg`, `.png`, or `.webp` extension; clearing the image cache does not clear them. In-flight uploads are staged under the local OS temp directory (never the object store) until committed, so staging never touches a network-mounted volume.
- `trickplay/<CODE>/<identity>/0.webp, 1.webp, …` — generated trickplay tile sheets, indexed by database rows.

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `ObjectStore:Path` | `ObjectStore__Path` | `<ContentRoot>/data/objects` | Directory of the durable object store. |

#### Cache Modes:
- **`LocalMovies`** *(default)*: Caches WebP versions of all local movie and actor images. Remote cover metadata images remain external redirects.
- **`Thumbnails`**: Generates and caches only thumbnail variants; full-size image requests serve original local files directly.
- **`AllMovies`**: In addition to local images, caches and crops remote metadata poster images.
- **`Disabled`**: Never writes to cache; serves local originals directly.

### R18.dev Reference Database

| Key | Environment Variable | Default | Description |
|---|---|---|---|
| `R18Dev:DumpSourceOverride` | `R18Dev__DumpSourceOverride` | *(none)* | Path or URL to an r18.dev database dump archive. When unset, uses standard upstream release URL. |

---

## Docker Compose Example

```yaml
version: "3.8"

services:
  javbuddy:
    image: javbuddy:latest
    container_name: javbuddy
    ports:
      - "5000:8080"
    environment:
      - ConnectionStrings__Default=Data Source=/config/Javbuddy.db
      - DataProtection__KeysPath=/config/dataprotection
      - ImageCache__Path=/cache
      - ObjectStore__Path=/objects
      - ImageCache__Mode=LocalMovies
      - ImageCache__MaxSizeMb=2048
      - ImageUpload__MaxSizeMb=20
      - Javinizer__BaseUrl=http://javinizer:8080
      - Javinizer__ApiToken=your-token-here
      - Jellyfin__BaseUrl=http://jellyfin:8096
      - Jellyfin__ApiKey=your-jellyfin-api-key
      - QBittorrent__BaseUrl=http://qbittorrent:8080
      - QBittorrent__Username=admin
      - QBittorrent__Password=adminadmin
      - QBittorrent__Category=jav
      - PathMapping__QBittorrentPrefix=/downloads/complete
      - PathMapping__JavinizerPrefix=/media/downloads
      - PathMapping__AppPrefix=/data/downloads
    volumes:
      - /host/path/to/config:/config        # DB + DataProtection keys — needs reliable POSIX locking
      - /host/path/to/cache:/cache          # Disposable WebP cache — fast local disk, safe to lose
      - /host/path/to/objects:/objects      # Durable object store (actor photos, trickplay) — bulk capacity, can be NFS
      - /host/path/to/media:/data:ro
    restart: unless-stopped
```

## Kubernetes Example

Illustrates the same three storage tiers as separate `PersistentVolumeClaim`s, each backed by a storage class suited to its access pattern:

```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: javbuddy-data
spec:
  accessModes: ["ReadWriteOnce"]
  storageClassName: ceph-block   # Replicated block storage for SQLite + DataProtection keys
  resources:
    requests:
      storage: 5Gi
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: javbuddy-cache
spec:
  accessModes: ["ReadWriteOnce"]
  storageClassName: local-path   # Fast local/single-replica disk for the disposable WebP cache
  resources:
    requests:
      storage: 20Gi
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: javbuddy-objects
spec:
  accessModes: ["ReadWriteMany"]
  storageClassName: nfs-client   # Bulk NFS storage for the durable object store
  resources:
    requests:
      storage: 100Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: javbuddy
spec:
  replicas: 1
  selector:
    matchLabels: { app: javbuddy }
  template:
    metadata:
      labels: { app: javbuddy }
    spec:
      containers:
        - name: javbuddy
          image: javbuddy:latest
          ports:
            - containerPort: 8080
          env:
            - name: ConnectionStrings__Default
              value: "Data Source=/data/Javbuddy.db"
            - name: DataProtection__KeysPath
              value: /data/dataprotection-keys
            - name: ImageCache__Path
              value: /cache
            - name: ObjectStore__Path
              value: /objects
          volumeMounts:
            - { name: data, mountPath: /data }
            - { name: cache, mountPath: /cache }
            - { name: objects, mountPath: /objects }
      volumes:
        - name: data
          persistentVolumeClaim: { claimName: javbuddy-data }
        - name: cache
          persistentVolumeClaim: { claimName: javbuddy-cache }
        - name: objects
          persistentVolumeClaim: { claimName: javbuddy-objects }
```
