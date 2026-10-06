---
name: dotnet-format-workflow
description: Use when checking or fixing .NET formatting in Javbuddy. Covers verification, scoped fixes, CI, and analyzer failures.
---

# dotnet format Workflow (Javbuddy)

`.editorconfig` at the repo root encodes this project's real formatting conventions (verified against the existing codebase, not aspirational defaults) plus the built-in .NET analyzers (`EnableNETAnalyzers`/`AnalysisLevel=latest` in `Javbuddy.csproj`). `AGENTS.md` requires running the check before considering any change done. This skill covers the mechanics — how to check, how to fix, and how the CI gate works — not the rules themselves (those live in `.editorconfig`; read it directly rather than guessing a rule's intent).

## When to Use This Skill

Use this skill before finishing any code change, and whenever a `dotnet format --verify-no-changes` failure needs fixing rather than just re-running.

## Check: `--verify-no-changes`

```bash
dotnet format Javbuddy.slnx --verify-no-changes
```

Exits non-zero and lists the files/rules it would change, without writing anything, if the codebase isn't already clean. Run this — not the bare `dotnet format` — when you just want to know whether something is clean (e.g. as a final check before considering a change done); it won't silently rewrite files you haven't reviewed.

## Fix: plain `dotnet format`

```bash
dotnet format Javbuddy.slnx
```

Applies the fixes in place. Run this when `--verify-no-changes` fails, then re-run `--verify-no-changes` (or just re-run the diff) to confirm it's now clean. Review the resulting diff before committing — `dotnet format` is generally safe (whitespace, using-directive order, expression-bodied-member conversions, etc.) but it's still a mechanical rewrite across the files it touches, worth a real look rather than a blind `git add`.

To limit the fix to files you actually changed rather than the whole solution (useful if an unrelated pre-existing violation elsewhere would otherwise get swept up in your diff):

```bash
dotnet format Javbuddy.slnx --include Javbuddy/Services/SomeFile.cs
```

## What Gets Checked

Two independent things flow through the same command:
1. **`.editorconfig` style rules** — e.g. `csharp_style_prefer_primary_constructors = true:warning`, `dotnet_style_readonly_field = true:warning`, `csharp_style_expression_bodied_lambdas = true:warning`, the using-directive ordering (`dotnet_sort_system_directives_first = true`, ungrouped — `dotnet_separate_import_directive_groups = false`, a deliberate deviation from the common default, verified against this codebase's actual `Services/*` convention rather than left at a tool default).
2. **Built-in .NET analyzers** — enabled via `EnableNETAnalyzers`/`AnalysisLevel=latest` in `Javbuddy.csproj`, independent of `.editorconfig`'s own rules.

Every rule in `.editorconfig` is set to `warning` severity or below — none are `error` — so a violation won't fail a plain `dotnet build`. It only becomes a hard failure via `--verify-no-changes`, when run locally. Don't assume a clean `dotnet build` means the formatting is clean; it doesn't check that.

## Before Committing

Run it locally before committing, as `AGENTS.md` requires (see Do's and Don'ts).

## Interpreting a Violation

The `--verify-no-changes` output names the rule id (an `IDE####` code) and the file/line — grep `.editorconfig` for the corresponding `csharp_style_*`/`dotnet_style_*` option (or the raw `dotnet_diagnostic.<id>` override, if one exists) if the intent isn't obvious from the message alone, since this project has already tuned several rules away from their stock defaults (see `dotnet_separate_import_directive_groups` above for one documented example). Don't hand-edit to satisfy a rule you don't understand; run the fix command instead and read the resulting diff.
