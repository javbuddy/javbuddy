# REST API and MCP server

Javbuddy exposes its library over a JSON REST API at `/api/v1` and over an [MCP](https://modelcontextprotocol.io) server at `/mcp`. Both are thin layers over the same service, so they see the same movies, tags, cast and `.nfo` state. Use the REST API for scripts and browser extensions (look up a code, add it, grab a release) and MCP to let an AI assistant such as Claude Code read and tidy up metadata.

> [!WARNING]
> **There is no authentication**, the same as the UI. Anyone who can reach the port can read the library, edit metadata, rewrite `.nfo` files and send torrents to qBittorrent. Run Javbuddy on a trusted network or behind a reverse proxy that adds its own authentication, and **never expose it to the internet**.

## Origin guard

A web page open in your browser could otherwise call the unauthenticated API on your behalf (cross-site requests, DNS rebinding). Every `/api/v1` and `/mcp` route therefore checks the `Origin` header:

- No `Origin` header (curl, scripts, MCP clients): allowed.
- An `Origin` equal to the request's own scheme and host: allowed.
- Any other `Origin`: **403** unless it is listed in `Api__AllowedOrigins`.

`Api__AllowedOrigins` is a comma-separated list of origins (`scheme://host[:port]`, compared case-insensitively), empty by default. A browser extension talking to Javbuddy needs its origin listed, for example `Api__AllowedOrigins=chrome-extension://abcdefghijklmnop`. See [Configuration](configuration.md).

> [!NOTE]
> The guard compares against the scheme and host of the request as Javbuddy sees it and does **not** read `X-Forwarded-*` headers. Behind a reverse proxy that terminates TLS, Javbuddy sees `http://...`, so a page loaded from `https://javbuddy.example.com` sends an origin that does not match and has to be listed in `Api__AllowedOrigins`.

Separately, a write request (anything but GET) with a body must be `application/json`, otherwise it gets **415**. This keeps a cross-site form post out of the write routes.

## Conventions

- JSON in camelCase; enums are camelCase strings (for example the `.nfo` `driftKind`: `none`, `javbuddyChanged`, `externalEdit`, `bothChanged`, `unreadable`).
- Movies are addressed by **code** in the path (`/movies/ABP-123`). Code matching ignores case and zero-padding, so `abp123` and `ABP-00123` find `ABP-123`. Responses always carry the canonical `code`, and a `webUrl` that opens the movie in Javbuddy.
- Errors are [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) problem details (`application/problem+json` with `title`, `status`, `detail`; `errors` for validation). An unknown `/api/v1` path also answers a 404 problem.

| Status | Meaning |
|---|---|
| 400 | Invalid request: a bad parameter, an unknown or contradictory metadata field, a bad date or enum. |
| 403 | The `Origin` header is not allowed (see above). |
| 404 | No movie, actor or tag with that code or id, or no such route. |
| 409 | `POST /movies` for a code already in the library. The problem carries the existing movie's `code` and `webUrl`. |
| 410 | The release to grab is no longer remembered (the 15-minute window passed): search again. |
| 415 | A request body that is not `application/json`. |
| 502 | An integration failed (javinizer-go, Prowlarr, qBittorrent). |
| 503 | The needed integration is not configured. |

## Routes

All routes are under `/api/v1`.

| Method and route | Purpose |
|---|---|
| `GET /system/info` | Javbuddy's version and which integrations (javinizer, prowlarr, qbittorrent, jellyfin) are configured. |
| `GET /movies` | List or search, newest added first. Query: `q`, `status` (`missing`/`got`), `studio`, `tag`, `actorId`, `nfoDrift`, `skip` (default 0), `take` (default 50, 1 to 200). Returns `{ total, items }`. |
| `GET /movies/lookup?codes=A,B,...` | Which of up to 100 codes are in the library (comma-separated or repeated `codes`). Returns `[{ query, code, inLibrary, movie }]`. |
| `GET /movies/{code}` | Full detail: metadata, status, tags, cast, files, `.nfo` drift, `webUrl`. |
| `POST /movies` | Add a code as Missing and fetch its metadata. Body `{ "code": "ABP-123" }`. 201, or 409 when present. |
| `PATCH /movies/{code}/metadata` | Partial metadata edit (below). |
| `PUT` / `DELETE /movies/{code}/tags/{tagId}` | Add or remove an existing tag. Both are no-ops when already in that state. |
| `POST /movies/{code}/cast` | Link a tracked actor. Body `{ "actorId": 12 }`. |
| `DELETE /movies/{code}/cast/{actorId}` | Unlink a tracked actor. |
| `DELETE /movies/{code}/cast?name=...` | Drop a cast name that matches no tracked actor. |
| `GET /movies/{code}/nfo` | `.nfo` drift state and the `.nfo` Javbuddy would write. |
| `POST /movies/{code}/nfo/push` | Write the `.nfo`. Optional body `{ "includeExternal": false }`; a `.nfo` edited outside Javbuddy is skipped unless true. |
| `POST /movies/{code}/javinizer/fetch` | Scrape through javinizer-go for comparison. Nothing is stored. |
| `GET /tags?q=` | Search the tag library (lists tags without `q`). |
| `GET /actors?q=` | Search tracked actors by name or alias (`q` is required). |
| `GET /movies/{code}/releases?q=` | Search Prowlarr (`q` defaults to the code). Works for codes not in the library. |
| `POST /movies/{code}/releases/grab` | Send a release from a recent search to qBittorrent. Body `{ "guid": "..." }`. |

The metadata fields are `title`, `titleOverride`, `originalTitle`, `description`, `releaseDate` (`YYYY-MM-DD`), `studio`, `label`, `series`, `director` and `contentType` (`censored`, `uncensored`, `decensored`). Runtime, rating and cover URLs are read-only.

## Examples

```sh
BASE=http://localhost:8080/api/v1

# Is it in the library? (tolerates case and zero-padding)
curl "$BASE/movies/lookup?codes=abp123,SSIS-001"

# Add it as Missing
curl -X POST "$BASE/movies" -H 'Content-Type: application/json' -d '{"code":"ABP-123"}'

# Edit metadata: "set" replaces, "clear" empties, everything else is kept
curl -X PATCH "$BASE/movies/ABP-123/metadata" -H 'Content-Type: application/json' \
  -d '{"set":{"description":"A new overview","releaseDate":"2023-05-01"},"clear":["series"]}'

# Search for releases, then grab one by its guid
curl "$BASE/movies/ABP-123/releases"
curl -X POST "$BASE/movies/ABP-123/releases/grab" -H 'Content-Type: application/json' -d '{"guid":"<guid from the search>"}'
```

`set` and `clear` may not name the same field, and unknown field names are rejected with 400. A patch changes the database only; the response carries a `warning` when something around the edit did not work.

### Search, then grab

A release search returns `[{ guid, title, indexer, size, seeders, leechers, ... }]`, most seeded first, plus a warning for each indexer Prowlarr skipped. The results have no download URL: Javbuddy remembers them for **15 minutes** per code and guid, and a grab names only the `guid`, so a client can never hand qBittorrent an arbitrary URL. If the movie is not in the library the grab adds it first. After the 15 minutes the grab answers 410 and the client searches again.

## OpenAPI

The machine-readable contract for `/api/v1` is served at `/openapi/v1.json`.

## MCP server

`/mcp` is a [Streamable HTTP](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#streamable-http) MCP endpoint, running **stateless**: every call stands alone, there are no sessions to lose on a restart. It has the same Origin guard and the same lack of authentication as the REST API. The tools call the same service as the REST routes and return the same JSON.

MCP covers reading and editing metadata, tags, cast and the `.nfo`. Adding a movie and grabbing releases are REST-only.

### Claude Code

```sh
claude mcp add --transport http javbuddy http://<host>:<port>/mcp
```

Add `--scope user` to make it available in every project. `/mcp` inside Claude Code shows the connection.

### Other clients

Any client that supports remote (Streamable HTTP) MCP servers needs only the URL `http://<host>:<port>/mcp` and the transport type "HTTP" (some call it "streamable-http"), with no credentials. A JSON config usually looks like:

```json
{ "mcpServers": { "javbuddy": { "type": "http", "url": "http://<host>:<port>/mcp" } } }
```

A client that only speaks stdio can use the `mcp-remote` bridge:

```json
{ "mcpServers": { "javbuddy": { "command": "npx", "args": ["mcp-remote", "http://<host>:<port>/mcp", "--allow-http"] } } }
```

### Tools

| Tool | What it does |
|---|---|
| `search_movies` | List or search movies (`query`, `status`, `studio`, `tag`, `nfo_drift`, `limit`, `offset`). |
| `get_movie` | One movie's full detail by code. |
| `fetch_javinizer_metadata` | Scrape a movie's metadata through javinizer-go to compare against; nothing is stored. |
| `update_movie_metadata` | Edit metadata fields with `set` and `clear`, leaving the rest untouched. |
| `list_tags` | Search the tag library; tags must come from here. |
| `add_movie_tag` / `remove_movie_tag` | Add or remove a tag by id. |
| `search_actors` | Search tracked actors; cast links need an id from here. |
| `add_movie_cast` / `remove_movie_cast` | Link a tracked actor by id, or remove a cast entry by actor id or unmatched name. |
| `preview_nfo` | Show a movie's `.nfo` drift and the `.nfo` Javbuddy would write. |
| `push_nfo` | Write the `.nfo` from the database (`include_external` also overwrites a `.nfo` edited outside Javbuddy). |

**Edits change the database only until `push_nfo`.** Metadata, tag and cast edits show up as `.nfo` drift on the movie; call `preview_nfo` to review and `push_nfo` to write the file into the media library. `push_nfo` is the one tool that changes files on disk. `push_nfo`, `remove_movie_tag` and `remove_movie_cast` are annotated as destructive, so MCP clients that honour tool annotations (Claude Code among them) may ask for confirmation before calling them.
