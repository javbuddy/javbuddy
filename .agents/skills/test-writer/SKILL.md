---
name: test-writer
description: Use when adding tests after implementing Javbuddy code. Adds focused coverage, runs it, and verifies formatting without changing production behavior.
---

# Javbuddy Test Writer

## When to Use

Use after a code change is implemented and needs focused xUnit, bUnit, or Playwright coverage. This is the verification step in `docs/feature-workflow.md`, not the feature implementation step.

## Scope

- Read `AGENTS.md` and the relevant production code before choosing coverage.
- Modify only `Javbuddy.Tests/` and `Javbuddy.E2ETests/`.
- Do not change production code to make a test pass. If testing reveals an application defect or a testability problem, report it to the parent task instead.
- Reuse existing test infrastructure rather than creating competing fixtures or doubles.

## Choose the Smallest Correct Test

- New non-trivial service method: add an xUnit test in `Javbuddy.Tests`.
- New non-trivial shared component: add a bUnit test.
- Pure mapper, normalizer, formatter, or helper: add a direct unit test without mocking.
- Browser/SignalR/CSS/JavaScript behavior: add a Playwright E2E test only when an in-process bUnit test cannot exercise the behavior.
- Do not duplicate bUnit coverage in the E2E suite.

Load the relevant project skills before writing tests:

- `bunit-component-testing` for Razor components.
- `nsubstitute-mocking` for service or component dependencies.
- `playwright-blazor` for a real browser/SignalR flow.

DB-backed tests reuse `Javbuddy.Tests/TestSupport/TestDbContextFactory.cs`, which uses an in-memory SQLite connection and the real provider behavior. E2E tests reuse `Javbuddy.E2ETests/Fixtures/E2EFixture.cs` and its isolated throwaway database.

## Procedure

1. Inspect the implementation diff and neighboring tests.
2. Identify the smallest externally observable behavior that proves the change.
3. Add or update focused tests in the matching existing test area.
4. Run `dotnet test Javbuddy.Tests/Javbuddy.Tests.csproj` when unit or component tests changed.
5. Run `dotnet test Javbuddy.E2ETests/Javbuddy.E2ETests.csproj` only when E2E tests changed.
6. Run `dotnet format Javbuddy.slnx --verify-no-changes`.
7. Report the tests added, why that level was chosen, and the exact command results.

## Delegating This Work

When the parent delegates to a subagent, the child brief must include:

- the repository path;
- the changed production files or diff scope;
- the behavior that needs proof;
- the requirement to read `AGENTS.md` and relevant project skills;
- the test-only file scope above;
- the exact verification commands;
- a request to return changed paths and command results.

A delegated child cannot ask the user questions. Keep ambiguous acceptance decisions in the parent session.

## Verification

The work is complete only when every changed test project passes and `dotnet format Javbuddy.slnx --verify-no-changes` exits successfully. A written test that was not executed is not verified.
