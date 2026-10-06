---
name: debug-ui
description: Use when debugging or verifying Javbuddy UI changes. Builds the app, drives it with a browser-automation tool, and checks browser/server failures.
---

# Debugging the Javbuddy UI

Javbuddy is a Blazor Server app (`@rendermode InteractiveServer`, prerendering
on). Unit tests and `dotnet build` prove the code compiles — they don't prove
a page renders right, a JS interop call fires, or a circuit doesn't crash on
connect. For anything UI-facing, run the real app and drive it with the
browser. This skill is the loop that actually worked across several rounds of
live debugging in this repo.

## 1. Build

```bash
dotnet build Javbuddy.slnx
```

**If the app is already running, this fails.** `dotnet build`/`dotnet run`
copies a fresh apphost into `bin/Debug/net10.0/`, and Windows won't let
it overwrite a file the running process has open — you'll see repeated
`MSB3026`/`MSB3027 Could not copy ... apphost.exe` retries and then a hard
failure. **Always stop the running instance before rebuilding**:

```bash
taskkill //F //IM Javbuddy.exe 2>/dev/null || true
```

Do this every time you're about to rebuild after already starting the app.

## 2. Run it

Start in Development mode so it picks up `appsettings.Development.json` (local
dev connection settings — javinizer-go, qBittorrent, Jellyfin, and a
`LocalLibrary:RootPaths` pointed at `test-jav/`, a local fixture library). Run it in the background so you can
keep working:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project Javbuddy --no-launch-profile
```
Start it with `terminal(background=true)` and leave `notify` unset because this
is a long-running server, not a bounded task. Keep the returned session ID and
use `process_manage` if you need to inspect or stop that background process.

`--no-launch-profile` skips `launchSettings.json` and falls back to Kestrel's
default `http://localhost:5000`. After starting, poll for readiness rather
than assuming a fixed startup time:

```bash
curl -s -o /dev/null -w "%{http_code}\n" --max-time 5 http://localhost:5000/
```

The background process starting successfully does **not** prove the app is
ready. Trust the separate `curl` health check. If you need to confirm the
native process is gone after stopping it, run:

```bash
tasklist //FI "IMAGENAME eq Javbuddy.exe"
```

Stop the app when done:

```bash
taskkill //F //IM Javbuddy.exe 2>/dev/null || true
```

## 3. Connect the browser

Use `browser_exec` with one named session for the whole verification. Batch
navigation, waiting, extraction, and related interactions into medium-sized
calls rather than making one call per click.

1. Open the page with `new_tab("http://localhost:5000/...")`, then call
   `wait_for_load()` and `page_info()`.
2. Prefer DOM/accessibility discovery over guessed coordinates. Use
   `cdp('Accessibility.getFullAXTree')` to locate a control and
   `cdp('DOM.getBoxModel', backendNodeId=...)` when coordinates are required.
3. Use `fill_input`, DOM-backed clicks, or `click_at_xy` only after locating the
   target. Wait for the resulting UI state, not an arbitrary fixed delay.
4. Call `capture_screenshot()` at meaningful checkpoints and inspect the
   attached image directly.
5. Read the resulting DOM and visible state back after each important action;
   a successful click call is not proof that the UI changed.
6. Reuse the named browser session for follow-up calls. Close disposable tabs
   before finishing.

For errors caused by subsequent interactions, install diagnostics after the
initial page load and read them back after the action:

```python
js(r'''(() => {
  window.__debugErrors = [];
  window.addEventListener('error', e =>
    window.__debugErrors.push({ type: 'error', message: e.message }));
  window.addEventListener('unhandledrejection', e =>
    window.__debugErrors.push({ type: 'rejection', message: String(e.reason) }));
  const originalError = console.error;
  console.error = (...args) => {
    window.__debugErrors.push({ type: 'console.error', message: args.map(String).join(' ') });
    return originalError.apply(console, args);
  };
  return true;
})()''')
print(js('window.__debugErrors || []'))
```

This does not recover errors emitted before instrumentation. Also inspect server
logs and check whether `#blazor-error-ui` became visible. If Browser Use is not
available, report the blocker after a real diagnostic attempt rather than
claiming UI verification from code inspection.

## 4. Read back what happened

- **Browser errors**: read `window.__debugErrors` and the visibility/text of
  `#blazor-error-ui` after the action that matters. Clear the diagnostics array
  before an isolated reproduction when you need to distinguish new failures
  from older ones.
- **Server-side logs**: use `process_manage` to poll the background terminal
  session or read the output path returned by the process tool. **.NET's Console
  output can be block-buffered when redirected to a file instead of a real
  terminal** — a file read can look stale even while the process is producing
  output. If you need to see a log line *now*
  (e.g. diagnosing a circuit crash), add a temporary `Console.WriteLine(...);
  Console.Out.Flush();` at the point you care about, rebuild (see the
  stop-first hint above), and reproduce. Remove the temporary logging before
  finishing — don't leave diagnostic `Console.WriteLine`s in committed code.
- **A circuit that connects then immediately disconnects** (`Error:
  Connection disconnected with error 'Server returned an error on close:
  Connection closed with an error.'` right after "WebSocket connected") is a
  server-side exception during that render, but it does **not** always show
  up in the normal `ILogger`/`CircuitHost` log output — don't trust "nothing
  in the log" as proof nothing broke. Reproduce the clean baseline in a
  separate sibling worktree or otherwise compare without discarding the user's
  working-tree changes. Use a **fresh browser tab** so a stale circuit and old
  diagnostics cannot contaminate the comparison.
- One concrete known cause worth checking first if you hit this: **Blazor's
  `PersistentComponentState` payload exceeding SignalR's default 32KB
  message-size limit** silently kills the circuit on reconnect with exactly
  this symptom. If a component persists a nontrivial list of EF entities,
  measure the actual JSON size before assuming the logic is wrong — a page
  of 60 full `Movie` rows serializes to ~45KB, over the limit; a trimmed
  per-field projection came in under 10KB. Quick way to measure without a
  browser round trip: query the same rows from `Javbuddy.db` with Python's
  built-in `sqlite3` module (see below) and `json.dumps(...)` them.

## 5. Test with real data, safely

There's no `sqlite3` CLI in this environment — use Python's built-in
`sqlite3` module for read-only inspection. This is an ad-hoc diagnostic query,
not application data access; production code still uses EF Core:

```bash
python -c "
import sqlite3
from pathlib import Path
db = (Path.cwd() / 'Javbuddy' / 'Javbuddy.db').resolve()
con = sqlite3.connect(f'file:{db.as_posix()}?mode=ro', uri=True)
cur = con.cursor()
cur.execute('SELECT COUNT(*) FROM Movies')
print(cur.fetchone())
"
```

Run this from the repository root. URI `mode=ro` ensures an inspection cannot
silently create or modify the database.

- `test-jav/` is a local-library fixture (a handful of movie
  folders with posters/nfo/video files) and is what
  `LocalLibrary:RootPaths` already points at in dev — safe to read from and
  safe to add throwaway rows/folders under, since it exists purely for this.
- **`appsettings.Development.json` points qBittorrent/javinizer-go/Jellyfin
  at real live services, not sandboxes.** Read-only calls (scan, list,
  status/config endpoints) are safe. javinizer-go's `Organize` step *writes
  files* to whatever its `allowed_directories` are configured to, which may be
  the user's real media library and download staging — **not** related to
  `test-jav`. Don't run a real Organize, don't add a real
  torrent grab, and don't run any other write/mutating call against a real
  external service without asking the user first — treat it like any other
  destructive-action judgment call.
- When a scenario needs scratch `Movie`/`Actor`/`TorrentDownload` rows, use the
  `seed-dev-db` skill and EF Core rather than raw SQL. Tag rows identifiably
  (for example, a distinctive `Notes` value) and remove them through the same
  disposable EF Core tool when finished.
- If you enable something in Settings (e.g. the r18.dev metadata source) or
  toggle a checkbox purely to test a flow, put it back to how it was before
  you finish, the same way you'd restore any other environment you borrowed.

## Checklist for a "did this actually fix it" pass

1. Stop any running instance → rebuild → confirm build is clean.
2. Start the app, confirm it's actually serving (`curl`, not just the task
   notification).
3. Drive the real feature through the browser — click through it the way a
   user would, not just load the page.
4. Read console messages and (if relevant) the server log for anything
   unexpected, applying the buffering caveat above.
5. If something's wrong and you're not sure it's your change, compare against
   the previous commit from a separate worktree and a fresh tab before theorizing.
6. Clean up: scratch DB rows, reverted settings toggles, closed tabs, stopped
   dev server.
7. Re-run `dotnet test`/`dotnet format --verify-no-changes` — a live-verified
   fix should still pass the deterministic checks too.
