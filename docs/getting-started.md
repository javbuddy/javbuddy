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

## Folder permissions

The Javbuddy container does not run as root. It runs as an unprivileged user (UID `1654` in the published image), so every host folder you mount must be accessible to that user. Folders that aren't produce access errors, even though the same path works fine for you on the host.

| Mount | What Javbuddy needs |
|---|---|
| `/data`, `/cache`, `/objects` | Read and write. They are empty folders owned by the app user when you use a named volume. A bind mount (`./javbuddy/data:/data`) uses the *host* folder's ownership instead. |
| `/media` (library) | Read to scan and play. Write to save NFO files, covers and extra images, or to delete and clean up movies. Don't mount it `:ro` unless you accept the warning below. |
| `/downloads` | Read and write, for merging VR parts. |

> **Warning: `:ro` on the library breaks several features.** A read-only `/media` still lets Javbuddy browse and play, but these fail with a permission error:
>
> - resolving NFO drift (writing the NFO back),
> - VR merge,
> - deleting movies,
> - adding extrafanart images to a movie.
>
> Mount the library read/write if you use any of them. Jellyfin is the one service in this setup that is fine with `:ro`.

If Docker creates a missing bind-mount folder for you, it is owned by root and the app user can't write to it. Create the folders yourself first (`mkdir -p javbuddy/data javbuddy/cache javbuddy/objects`).

### Check who the container runs as

```bash
docker exec javbuddy id          # uid=1654 gid=1654 ...
docker inspect javbuddy --format '{{.Config.User}}'
```

### Check the host folder and the mount

```bash
ls -ldn /srv/jav/library                  # numeric owner, group and mode of the folder
ls -ln /srv/jav/library | head            # and of what's inside it
docker exec javbuddy ls -ld /media        # the same folder as the container sees it
docker exec javbuddy touch /media/.write-test && docker exec javbuddy rm /media/.write-test
```

If `/media` is empty or missing inside the container, the mount itself is wrong (check the `volumes:` line and the host path). If it is there but the `touch` fails, it is a permission problem.

### Fix it

Pick one. Don't use `chmod 777`, which lets every user and process on the host change your library.

- **Give the folder to the container user.** Simplest when only Javbuddy writes to the folder.

  ```bash
  sudo chown -R 1654:1654 /srv/jav/library javbuddy/data javbuddy/cache javbuddy/objects
  ```

- **Share a group** when other services (javinizer-go, qBittorrent, Jellyfin) or you also use the library. Give the folder to a shared group, make it group-writable, and run the containers with that group:

  ```bash
  sudo groupadd -g 2000 media          # pick an unused GID
  sudo chgrp -R media /srv/jav/library
  sudo chmod -R g+rwX /srv/jav/library
  sudo find /srv/jav/library -type d -exec chmod g+s {} +   # new files inherit the group
  ```

  ```yaml
  services:
    javbuddy:
      group_add:
        - "2000"
  ```

- **Run as your own user** so the container matches the folder owner. Find your IDs with `id -u` and `id -g`, then set `user: "1000:1000"` on the service. Also `chown` `/data`, `/cache` and `/objects` to that user.

- **SELinux hosts** (Fedora, RHEL): ownership can be fine and access still denied. Add `:z` to the bind mount (`/srv/jav/library:/media:z`) so the container may use the folder. Use `:Z` only for folders no other container shares.

### Common errors

| Error | Likely cause |
|---|---|
| `UnauthorizedAccessException: Access to the path '/media/...' is denied` or `Permission denied` when saving an NFO, image or moving a movie | The library is read-only for the container user, or mounted `:ro`. |
| Library Import or the rescan finds no folders, or a library folder shows as missing | The container user can't read the folder (no read or execute bit on a parent folder), or the mount path is wrong. |
| `SQLite Error 14: 'unable to open database file'` or `attempt to write a readonly database` at startup | `/data` isn't writable by the container user (often a root-owned bind mount). |
| The app can't write `dataprotection-keys`, or sessions reset on every restart | The same: `/data` is not writable. |
| It worked as root or in a test, but not in the container | The path is accessible to you but not to UID `1654`. Compare `ls -ldn` on the host with `docker exec ... id`. |

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
