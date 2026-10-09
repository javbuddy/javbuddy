---
name: github-issues
description: Use when working with Javbuddy GitHub issues. Covers backlog lookup, repository labels and templates, issue creation, comments, and linking implementation work.
---

# GitHub issues

Javbuddy's backlog is tracked in GitHub issues (`github.com/javbuddy/javbuddy`); consult the relevant issue as the feature spec when one exists. Use this skill to find, create, update, or link an issue as part of work on Javbuddy.

## Prerequisite

The GitHub CLI (`gh`) must be installed and authenticated (`gh auth status`). If it is unavailable, use the GitHub web interface when available. Do not claim an issue was checked or updated if neither route worked.

## Label taxonomy

The project label set supplied by the user is:

- **Area labels** — use one for the top-level part of the app an issue affects:
  - `Area::Movies` — Movies, MovieAdd, MovieDetail and their subpages, including MovieScenes and MovieTags
  - `Area::Actors` — Actors and actor add, detail, edit, import, missing, and photo flows
  - `Area::Missing` — Missing
  - `Area::Settings` — Settings and its connection, metadata, and UI sections
  - `Area::TorrentSort` — TorrentSort
  - `Area::LibraryImport` — LibraryImport
  - `Area::Activity` — ActivityHistory and ActivityQueue
  - `Area::System` — System status, statistics, tasks, and infrastructure/deployment concerns without their own page
  - `Area::Scenes` is not in the supplied set; do not use it unless it is added. The existing `/movies/scenes` feature belongs to `Area::Movies`.
  - A proposed new page may omit an Area label until that page exists.
- **Work type** — use applicable labels from `bug`, `Improvement`, `feature`, `Idea`, `Refactor`. GitHub already had the default lowercase `bug` label, which covers the GitLab `Bug` label. Use `brainstorm` for an issue explicitly in the brainstorming stage and `Shelved` only for work intentionally parked; these are workflow labels, not substitutes for an Area label.
- **Domain / cross-cutting labels** — apply only when the issue materially concerns that domain: `Ai`, `Api`, `Filesystem`, `Jellyfin`, `Mobile`, `Prowlarr`, `Torrent`, `cache`, `javinizer-go`, `ui`.

Potential additions to consider if they address recurring backlog items:

- `Area::Documentation` for documentation-only issues that do not fit an app page or system/deployment work.
- `Performance` for performance work that is not clearly covered by a specific domain label.
- `Security` for security issues and hardening work.

The repository's current templates can be reviewed under `.github/ISSUE_TEMPLATE/`. When creating from the CLI, provide a body based on the actual template or use `gh issue create --template <name>`; do not assume the CLI automatically applies a web issue template.

## Common commands

Run these from the repository root so `gh` selects the right repository, or pass `--repo javbuddy/javbuddy` explicitly.

```sh
# Check authentication and the configured repository
gh auth status
gh repo view --json nameWithOwner,url

# Find the feature spec or inspect the backlog
gh issue list --search "<terms>"
gh issue view <number> --comments
gh issue list --label "<existing-label>"

# Inspect labels and templates before filing
gh label list
find .github/ISSUE_TEMPLATE -maxdepth 2 -type f -print

# File or update an issue using verified labels
gh issue create --title "..." --body "..." --label "<existing-label>"
gh issue edit <number> --add-label "<existing-label>"
gh issue edit <number> --remove-label "<existing-label>"
```

Creating, editing, closing, or commenting on a live issue changes GitHub. Do so only when the user explicitly asks for that action or their task clearly authorizes that specific issue update. Finding and reading issues is read-only.

## Commenting on an issue

When the user has authorized issue comments, use `gh issue comment <number> --body-file <file>` for multiline content. Avoid shell interpolation for long Markdown bodies. A comment ends with a signature naming the model actually doing the work, never a hardcoded model name:

```text
Co-Authored-By: <Model name and version> (<provider>)
```

For an issue-driven implementation, use the milestones from `docs/feature-workflow.md`:

- **Start** — before implementation, briefly say work is starting.
- **Finish** — after implementation and verification, summarize what changed and how it was verified. Include the implementation plan and its completed `## Improvements & Issues Resolved` section when the issue workflow calls for the full feature plan.

Do not post intermediate plan drafts. Post only milestones the user has authorized. A plan belongs in the issue when that is the agreed issue workflow; do not add a disposable `docs/<feature>-plan.md` file.

## Workflow integration

For work beyond a trivial fix, first search for an existing issue. Treat its description as the spec, while applying the user's latest request as the authority if they clarify or change scope. If no issue exists, create one only when issue creation is authorized; otherwise continue from the user's request without inventing issue requirements.

Follow `docs/feature-workflow.md` for planning, implementation, and verification. Do not commit, push, create a pull request, or post a GitHub comment unless that action is authorized. When an authorized commit addresses an issue, include `Refs #<number>` or `Closes #<number>` in its message as appropriate. For a pull request, use GitHub's closing keyword in the PR description (for example, `Closes #<number>`) when it should close the issue on merge.
