# Feature Development Workflow

Formalizes the request → plan → verify → implement → verify → git shape this project already uses in practice — every claim in a plan tagged with how it was verified against the real running system, not assumed.

## When to create an implementation plan

Create an implementation plan before implementing when the feature touches more than one layer (data model + service + UI) or an external integration (javinizer-go, Prowlarr, Jellyfin, qBittorrent). A trivial one-file fix or a small bug fix can skip straight to implementation.

Do not write or commit the plan to a repository file (no `docs/<feature>-plan.md`). Instead, hold the plan in session context/scratch space during development.

For something bigger — several distinct workstreams, expected to span multiple sessions — use **docs/blueprint-template.md** instead (or in addition, for workstreams that need their own design decisions).

## 1. Request

Treat the user's request as the spec. Implement what's asked, no more, no less. If it is ambiguous or silent on a detail needed to implement it, ask rather than guess.

## 2. Plan

For the "Research findings" step below, delegate multi-area "where is X implemented" / "how does Y currently work" lookups with a subagent rather than keeping raw search noise in the planning session. Give the child the repository path, relevant issue/spec context, the read-only constraint, and the required `path:line` evidence. A small, single-file check doesn't need delegating.

Structure the plan with this section skeleton (held in context/scratch, not committed as a file):

- `# <Feature> — Implementation Plan`
- `## Goal` — short paragraph: what's being built and why. Not a research narrative.
- `## Research findings` — verified facts only, one bullet/subsection per topic. Each bullet states what was checked and against what real system ("confirmed live via `GET /api/v1/config` against the real instance", "verified by grepping the codebase for X"). This is the auditable evidence trail for the "verify against the real thing" rule below — kept separate from opinions/decisions so it's scannable on its own.
- `## Decision / Approach` — the chosen approach and why, informed by the findings above.
- `## Non-goals` — the scope boundary, stated before implementation detail so a reader knows what's excluded up front.
- `## Implementation` — one parent heading, with subheadings per area as needed: `### API/integration contract` (if applicable), `### Data model changes`, `### Service changes`, `### UI`.
- `## Verification plan` — remaining things to check *while building* (distinct from "Research findings," which is what was already verified before writing the plan).
- `## Critical files` — the files this touches.
- `## Improvements & Issues Resolved` — left empty when the plan is first written; appended to during/after implementation (see step 5).

## 3. Verify the plan

Before implementing, confirm assumptions against the real running system (`AGENTS.md`: "verify against the real thing, not assumptions" — read the actual source of an external API, or run the app and exercise the feature, rather than guessing field names or claiming something works from reading code alone). If the spec turned out ambiguous during planning, ask the user now, before writing code.

## 4. Implement

Follow the layering in `docs/architecture.md` (components call services, not `IDbContextFactory`/integration clients directly beyond a single read-only query) and the UI conventions in `docs/ui-guidelines.md`.

## 5. Verify the implementation

- Run the relevant tests per `AGENTS.md`'s Testing section (new service method → xUnit test; new shared component → bUnit test; pure-function helper → cheap unit test). Add an E2E test only for flows that specifically need a real browser/SignalR circuit.
- Run `dotnet format Javbuddy.slnx --verify-no-changes`.
- For UI changes, actually run the app and exercise the feature in a browser — load the `debug-ui` skill and, when browser output would flood the main context, delegate the verification with a complete subagent brief — rather than claiming a UI works from reading the code alone.
- Append what you found during implementation to the plan's `## Improvements & Issues Resolved` section (root cause + fix, per bug) — don't rewrite the plan itself.

## 6. Update CHANGELOG.md

Add an entry under `## [Unreleased]` as part of the same change, not a follow-up step.

## 7. Git

See `AGENTS.md`'s "Git hygiene" section for the commit message convention and rules (don't commit unless asked).
