# Getting Started

This guide takes you from nothing to a working Javbuddy with metadata, downloads and your existing collection. For every setting, see the [configuration reference](configuration.md). For how to lay out and import a library, see the [library guide](library-guide.md).

## What you need

| Service | Needed for | Required |
|---|---|---|
| Docker with Compose | Running Javbuddy | Yes |
| [javinizer-go](https://github.com/javinizer/javinizer-go) | Movie metadata, cover images, and organizing finished downloads | Yes |
| [Prowlarr](https://prowlarr.com/) | Searching torrent indexers | Only for downloading |
| [qBittorrent](https://www.qbittorrent.org/) | Downloading | Only for downloading |
| [Jellyfin](https://jellyfin.org/) | Ownership sync and "Open in Jellyfin" | No |

Javbuddy has no login of its own, so run it on a trusted network or behind an authenticating reverse proxy.

You also need a folder for your library (even an empty one) and, if you download, a folder for finished downloads.

## 1. Plan your folders

Javbuddy, javinizer-go and qBittorrent all touch the same files. The simplest setup is to mount the same host folders at the **same path inside every container**:

| Host folder | Mounted at | Used by |
|---|---|---|
| `/srv/jav/library` | `/media` | Javbuddy (read/write), javinizer-go (read/write), Jellyfin (read-only) |
| `/srv/jav/downloads` | `/downloads` | qBittorrent (read/write), javinizer-go (read/write), Javbuddy (read/write, for merging VR parts) |

With identical paths everywhere you don't need any [path mappings](library-guide.md#when-the-services-see-different-paths). The examples below assume this layout. Adjust the host paths to yours.

## 2. Deploy the services

Javbuddy's [`docker-compose.yml`](../docker-compose.yml) runs Javbuddy only. Run the other services next to it. The compose file below is an example based on each project's own image. Check their documentation for current options.

```yaml
services:
  javbuddy:
    image: ghcr.io/javbuddy/javbuddy:latest
    ports:
      - "8080:8080"
    environment:
      LocalLibrary__RootPaths__0: /media
    volumes:
      - ./javbuddy/data:/data
      - ./javbuddy/cache:/cache
      - ./javbuddy/objects:/objects
      - /srv/jav/library:/media
      - /srv/jav/downloads:/downloads
    restart: unless-stopped

  javinizer:
    image: ghcr.io/javinizer/javinizer-go:latest
    ports:
      - "8765:8765"
    volumes:
      - ./javinizer:/javinizer
      - /srv/jav/library:/media
      - /srv/jav/downloads:/downloads
    restart: unless-stopped

  qbittorrent:
    image: lscr.io/linuxserver/qbittorrent:latest
    ports:
      - "8081:8080"
    volumes:
      - ./qbittorrent:/config
      - /srv/jav/downloads:/downloads
    restart: unless-stopped

  prowlarr:
    image: lscr.io/linuxserver/prowlarr:latest
    ports:
      - "9696:9696"
    volumes:
      - ./prowlarr:/config
    restart: unless-stopped
```

Start it with `docker compose up -d`. Services in the same compose project reach each other by service name, e.g. `http://javinizer:8765`. From your browser, use the published ports (`http://localhost:8765`, …).

### javinizer-go

1. Open javinizer-go's web UI (`http://localhost:8765`) and create the admin login it asks for on first start.
2. Set its output folder format to the bare release code, so every movie folder is named `ABCD-123` (see [folder layout](library-guide.md#folder-layout)). In javinizer-go's `config.yaml`, under `output:`, set `folder_format: <ID>` (and `file_format: <ID>` for the video file name).
3. Create an API token for Javbuddy with `javinizer token create`, run inside the javinizer-go container (`docker compose exec javinizer javinizer token create`). Keep the token for step 4 below.

### qBittorrent

1. Open its web UI and set a username and password (**Options → Web UI**). The linuxserver image prints a temporary password in its log on first start.
2. Set the default save path to `/downloads`.
3. Optionally pick a category (e.g. `jav`) for Javbuddy to assign to the torrents it adds; you enter it in Javbuddy's connection settings below.

### Prowlarr

Open its web UI, add your indexers (**Indexers → Add Indexer**), and copy the API key from **Settings → General**.

## 3. First run in Javbuddy

Open <http://localhost:8080>.

1. **Settings → Connections** (`/settings/connections`). Enter each service and press its test button:
   - **javinizer-go**: URL `http://javinizer:8765` and the API token from above. This is required.
   - **Prowlarr**: URL `http://prowlarr:9696` and its API key.
   - **qBittorrent**: URL `http://qbittorrent:8080` (the port *inside* the compose network), username, password, and optionally the category.
   - **Jellyfin** (optional): URL and an API key from Jellyfin's **Dashboard → Advanced → API Keys**. See the [Jellyfin guide](jellyfin-integration.md).
   - **Destination aliases**: named folders that Torrent sort can move finished downloads into. They can only be set through environment variables on Javbuddy, e.g. `Javinizer__DestinationAliases__0__Name: jav` and `Javinizer__DestinationAliases__0__Path: /media/jav`. The path is as *javinizer-go* sees it, and should be inside one of your library roots.

   Anything you set through an environment variable locks that field in the UI. Anything you leave unset is configured in the UI.
2. **Settings → Metadata** (`/settings/metadata`): check that your library folder is listed, and enable the optional metadata sources (r18.dev, WAPdB) if you want them.
3. Bring in what you already have with **Movies → Library Import** (`/add/import`), see the [library guide](library-guide.md#importing-an-existing-collection). Or start a wanted list with **Movies → Add New** (`/add/new`) by typing a release code.
4. **System → Tasks** (`/system/tasks`): review the schedules and run the first library rescan, image cache and r18.dev import now instead of waiting.
5. **System → Status** (`/system/status`) shows the health of every integration and your disk space. Check it if something doesn't connect.

## 4. Get your first movie

1. Add a movie by code with **Add New**. Javbuddy fetches its metadata through javinizer-go and tracks it as **Missing**.
2. Open it and search Prowlarr from the movie page. Sending a result to qBittorrent marks the movie **Downloading**.
3. When the download finishes, open it in **Activity** and use **Sort**. This runs the [Torrent sort wizard](library-guide.md#sorting-a-finished-download), which moves the files into your library. The movie becomes **Got**.

## Updating

```bash
docker compose pull && docker compose up -d
```

Javbuddy keeps its database in `/data`, so keep that volume (back it up). Your library itself lives outside the container.
