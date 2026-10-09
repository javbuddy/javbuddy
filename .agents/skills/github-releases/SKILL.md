---
name: github-releases
description: Use when preparing a Javbuddy release on GitHub. Covers SemVer version bumps, changelog-based release notes, version tags, draft releases, and pre-1.0 preview status.
---

# Javbuddy GitHub releases

Use this skill when preparing or managing a release for `javbuddy/javbuddy`. Follow `AGENTS.md` and `CHANGELOG.md` for project versioning rules. A release should be prepared as a GitHub draft first; publishing it is a separate action that needs explicit user authorization.

## Release conventions

The current release history establishes these conventions:

- Git tag: `v<major>.<minor>.<patch>` (for example, `v0.1.2`).
- GitHub release title: `<major>.<minor>.<patch>` without the `v` prefix.
- Release notes: the corresponding `CHANGELOG.md` entry, retaining its Keep a Changelog headings and bullets, followed by `Docker image: \`ghcr.io/javbuddy/javbuddy:<version>\``.
- Releases below `1.0.0` are GitHub pre-releases. The existing `v0.1.0`, `v0.1.1`, and `v0.1.2` releases all use this setting.

The `CI` workflow runs on published releases. It runs formatting, unit and E2E checks, then builds and publishes the container. The release workflow tags the image with the full SemVer version and its major/minor aliases. Creating a draft does not start that published-release workflow; publishing does.

## Prepare a release

1. Check `gh auth status`, confirm the repository with `gh repo view --json nameWithOwner,url`, and inspect `gh release list` plus the latest tag. Do not assume local tags or an old release list are current.
2. Determine the next version from the unreleased changes and SemVer policy in `AGENTS.md` and `CHANGELOG.md`. Use the version in `Directory.Build.props` as the current project version. If the requested release version or the required bump is unclear, ask rather than guess.
3. Confirm the release changes are complete and verified. The matching `CHANGELOG.md` section must be dated, and `Directory.Build.props` must contain the same version. Do not invent release notes that are absent from the changelog.
4. Confirm the release commit is available on GitHub and that no tag or release already uses the proposed version. Do not move or overwrite an existing tag/release.
5. Prepare notes from that exact changelog section. Keep its headings and bullets, omit the changelog's dated version heading, and append the Docker image line. Use `First release.` only for the first release, following `v0.1.0`'s format.
6. Create the release as a draft. For versions below `1.0.0`, set the pre-release flag. Set the tag to `v<version>`, title to `<version>`, and target to the verified release commit. GitHub CLI can create a missing tag from the specified target commit.

Example for a pre-1.0 draft, after substituting the actual version, release commit, and notes file:

```sh
gh release create v0.1.3 \
  --repo javbuddy/javbuddy \
  --title 0.1.3 \
  --notes-file /tmp/javbuddy-release-notes-0.1.3.md \
  --target <release-commit> \
  --draft \
  --prerelease
```

For versions `1.0.0` and later, omit `--prerelease` unless the user explicitly requests a preview release. Keep `--draft` until the user authorizes publishing.

## Verify and hand off

After creating the draft, inspect it with `gh release view v<version> --repo javbuddy/javbuddy` and confirm the title, tag, target, notes, draft state, and pre-release state. Report the draft URL and any remaining steps.

Do not publish the draft, move/delete tags, or modify an existing release unless the user explicitly authorizes that action. Publishing a release triggers CI and publishes container images, so make publishing a deliberate separate step. When authorized, publish with:

```sh
gh release edit v<version> --repo javbuddy/javbuddy --draft=false
```
