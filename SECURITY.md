# Security policy

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub's **Report a vulnerability** button on the repository's **Security** tab (private vulnerability reporting). Don't open a public issue or pull request for it.

Include what you found, how to reproduce it, and the version or commit you tested. You'll get a reply as soon as the maintainer can manage; this is a hobby project, so there is no fixed response time.

## What to keep in mind

Javbuddy has no login of its own. It is meant to run on a network you trust, or behind a reverse proxy that adds authentication. Anyone who can reach it can read and change the library, and can use it to make requests to the services it's configured with (javinizer-go, Prowlarr, qBittorrent, Jellyfin). Reports that boil down to "there is no authentication" are known and not treated as vulnerabilities.
