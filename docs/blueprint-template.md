# Feature Blueprint (Template)

A reusable template for planning and tracking a **large, multi-session effort** — one with several distinct workstreams, expected to span many commits and possibly multiple Claude sessions, where a durable checklist is what lets a later session (or a fresh Claude instance with no memory of this conversation) pick up exactly where things left off.

## When to use this vs. a single plan doc

- A feature that's plannable and implementable in roughly one sitting/PR → use `docs/feature-workflow.md`'s `## 2. Plan` skeleton (`docs/<feature>-plan.md`) instead. That's a design doc: research findings, a decision, an implementation plan.
- Something bigger — multiple workstreams, spans several sessions, needs a resumable checklist — use this blueprint instead (or alongside a design doc, for the workstreams that need one).

## Template

Copy this shape into a new `docs/<effort-name>-plan.md` when starting:

```markdown
# <Effort name> — Progress Tracker

One or two sentences: what this effort is, why now, and what's explicitly
out of scope.

Update checkboxes as each item lands, in the same commit (or immediately
after) that completes it.

## Guiding constraints

- Anything that must hold true across every workstream (e.g. "no
  application code changes", "stays backward compatible with X").

## Workstream A — <name>

- [ ] Concrete, checkable item
- [ ] Another one

## Workstream B — <name>

- [ ] ...

## Build order

1. Which workstream depends on which, and why (e.g. "docs before the
   AGENTS.md pointers that reference them").

## Verification (final pass, once all workstreams land)

- [ ] Concrete, checkable verification step (a command to run, a fact
  to confirm) — not "make sure it works."

## Status: <update when done>
```

Add workstreams as you discover them — this doesn't need to be complete on day one. If a workstream turns out to need its own design decisions, write a regular plan doc for it (`docs/<workstream>-plan.md`) and link it from here instead of expanding this tracker into a design doc itself.

## Lifecycle

1. Copy the template above to `docs/<effort-name>-plan.md`.
2. Fill in real workstreams/checklists as the effort gets scoped.
3. Check items off as they land, in the same commit that completes them (or immediately after) — this is what makes the doc useful across sessions.
4. Once every workstream and the final verification pass are checked off, the tracker has done its job. **Delete it.** `CHANGELOG.md` and git history are the durable record of what shipped and why; a completed tracker left in `docs/` is just stale documentation waiting to drift from reality (the code, its tests, and the other `docs/*.md` files it produced are the real, durable artifacts).
