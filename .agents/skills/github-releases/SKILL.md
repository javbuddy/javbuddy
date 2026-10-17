---
name: github-releases
description: Use when preparing a Javbuddy release on GitHub. Covers SemVer version bumps, changelog-based release notes, version tags, draft releases, and pre-1.0 preview status.
---

# Javbuddy GitHub releases

Use this skill when preparing or managing a release for `javbuddy/javbuddy`. Follow `AGENTS.md` and `CHANGELOG.md` for project versioning rules. A release should be prepared as a GitHub draft first; publishing it is a separate action that needs explicit user authorization.

## Release conventions

The current release history establishes these conventions:

- Git tag: `v<major>.<minor>.<patch>` (for example, `v0.1.2`).
- GitHub release title: the tag itself, `v<major>.<minor>.<patch>` (for example, `v0.3.2`). Every release since `v0.2.0` uses this; only `v0.1.0`–`v0.1.2` were titled without the `v`.
- Release notes: the corresponding `CHANGELOG.md` entry, retaining its Keep a Changelog headings and bullets, always ending with a blank line and `Docker image: \`ghcr.io/javbuddy/javbuddy:<version>\`` (the version without the `v`).
- Releases are not marked as GitHub pre-releases. Only `v0.1.0`–`v0.1.2` were; every release since `v0.2.0` is a normal release, even though the version is still below `1.0.0`.

The `CI` workflow runs on published releases. It runs formatting, unit and E2E checks, then builds and publishes the container. The release workflow tags the image with the full SemVer version and its major/minor aliases. Creating a draft does not start that published-release workflow; publishing does.

## Prepare a release

1. Check `gh auth status`, confirm the repository with `gh repo view --json nameWithOwner,url`, and inspect `gh release list` plus the latest tag. Do not assume local tags or an old release list are current.
2. Determine the next version from the unreleased changes and SemVer policy in `AGENTS.md` and `CHANGELOG.md`. Use the version in `Directory.Build.props` as the current project version. If the requested release version or the required bump is unclear, ask rather than guess.
3. Confirm the release changes are complete and verified. The matching `CHANGELOG.md` section must be dated, and `Directory.Build.props` must contain the same version. Do not invent release notes that are absent from the changelog.
4. Confirm the release commit is available on GitHub and that no tag or release already uses the proposed version. Do not move or overwrite an existing tag/release.
5. Prepare notes from that exact changelog section. Keep its headings and bullets, omit the changelog's dated version heading, and append the Docker image line. Use `First release.` only for the first release, following `v0.1.0`'s format.
6. Create the release as a draft, without the pre-release flag. Set the tag to `v<version>`, title to `v<version>`, and target to the verified release commit. GitHub CLI can create a missing tag from the specified target commit.

Example draft, after substituting the actual version, release commit, and notes file (whose last line is the Docker image line):

```sh
gh release create v0.3.2 \
  --repo javbuddy/javbuddy \
  --title v0.3.2 \
  --notes-file /tmp/javbuddy-release-notes-0.3.2.md \
  --target <release-commit> \
  --draft
```

Add `--prerelease` only when the user explicitly asks for a preview release. Keep `--draft` until the user authorizes publishing.

## Verify and hand off

After creating the draft, inspect it with `gh release view v<version> --repo javbuddy/javbuddy` and confirm the title, tag, target, notes, draft state, and pre-release state. Report the draft URL and any remaining steps.

Do not publish the draft, move/delete tags, or modify an existing release unless the user explicitly authorizes that action. Publishing a release triggers CI and publishes container images, so make publishing a deliberate separate step. When authorized, publish with:

```sh
gh release edit v<version> --repo javbuddy/javbuddy --draft=false
```
