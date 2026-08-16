# AGENTS.md

Guidance for an AI coding agent (and any human) working in the **Pivotask** feature
(`features/ManageTasks/`). Read the repo-wide [`AGENTS.md`](../../AGENTS.md) first (build/test/format
gates, git workflow, the one-way layering rule), then this, then the linked docs. This mirrors the
conventions of the sibling features — same repo, same tooling, same golden-rule discipline.

## What this project is

**Pivotask** is a Trello-inspired task board. One board lives in one JSON file, named with the
extension **`.pivotask`**. Its CLI is **`ptask`**.

It is the family's **first non-text editor**, and that changes what it shares: not
`Puffinote.Text` — there is no piece table here, no grapheme navigation, no encoding round-trip — but
`Puffinote.Components` (theming, overlays, commands) and `Puffinote.Components.Core` (CLI scaffolding),
which is exactly what those libraries are for.

Start with [docs/PRD.md](docs/PRD.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). The design
rationale is [proposal 0004](../../docs/proposals/0004-pivotask.md), and the
[prototype](../../pivotask-prototype/pivotask-prototype.html) is the behavioural spec it was drawn from.

## Golden rules (non-negotiable)

1. **`Pivotask.Core` has ZERO UI dependencies.** No `Avalonia.*`. The model, codec, commands, and
   filters are pure and testable without a window. This is where most logic and tests live.
2. **The control uses STOCK AVALONIA CONTROLS, deliberately.** Typage and Cellestra draw their own
   surfaces because no stock control can be a live-preview text editor or a spreadsheet grid. A board
   is `ItemsControl` + panels + drag-and-drop, where custom drawing would cost thousands of lines and
   buy nothing. **Do not "correct" this** because a sibling area draws its own surface (proposal 0004 §5).
3. **Every mutation is an undoable command.** `BoardCommands` is the only way to change a board — the
   GUI, the CLI, and a test all go through it, so nothing can change a board un-undoably.
4. **The format's rules are the format's design** (§4). Deterministic bytes, unknown fields preserved,
   a version ahead of ours refused, atomic save. This file lives in git; every one of those exists so it
   stays reviewable and survives being opened by an older build.
5. **A board is matched by `EndsWith`, not by `Path.GetExtension`.** The extension is `.pivotask` now,
   but boards written before the rename are `.pivotask.json` — for which `GetExtension` returns
   `".json"` — and those still open. So a board is matched by suffix, against both spellings
   (`DocumentTypes.IsBoard` / `Workspace.Suffix` + `LegacySuffix`), and always ahead of any generic
   `.json` route. There is a regression test for the routing in `Puffinote.UiTests`. Only `.pivotask` is
   ever *written*, and only it is claimed from Finder.
6. **No scope creep toward a project manager.** No assignees, no recurrence, no times or timezones, no
   dependencies, no nesting beyond the one `steps[]` level, no cross-notebook aggregation. See §8 of the
   proposal, restated in the PRD's non-goals.
7. **Guard the supply chain.** `System.Text.Json` is BCL, which is why the format is JSON and needs no
   package. New dependencies go through `Directory.Build.targets`' allowlist with a stated reason.

## Where things go

- **The model, the codec, mutations, filters** → `Pivotask.Core/`.
- **The board, the list, the task dialog, and every command that acts on the board** →
  `Pivotask.Control/`. Puffinote embeds this.
- **Shell** → `Pivotask.Desktop/` — window, menu, file dialogs, status bar.
- **CLI** → `Pivotask.Cli/` (`ptask`). A full mutating verb set: a task CLI that can't add a task isn't
  worth shipping (§10.5).
- **Tests** → `Pivotask.Core.Tests/` (most of them), `Pivotask.UiTests/`, `Pivotask.Cli.Tests/`.

## Everyday commands

```bash
just build-pivotask
just test-pivotask
just test-filter SomeTest pivotask
just run-pivotask-cli list --due-tomorrow
just check                        # format-verify + build + test EVERY area — the gate
```

**Definition of done:** `just check` passes. Warnings are errors, so "builds" means zero warnings.

**Verifying changes.** The devcontainer is headless: `just run-pivotask` fails environmentally, and that
is not a regression. Verify through `Pivotask.Core.Tests`, `Pivotask.UiTests`, or `ptask` — the CLI is a
first-class way to check behaviour here, which is much of why it landed before the GUI.

## Working in this codebase (gotchas)

- **The namespace `Pivotask.Control` shadows `Avalonia.Controls.Control`.** Inside the control library,
  write `Avalonia.Controls.Control` when you mean the type. (Plaintain has the same problem for the same
  reason.)
- **The board rebuilds wholesale on every change** rather than diffing. A board is tens of tasks, so a
  rebuild is cheap and it removes the class of bug where a view has quietly stopped agreeing with the
  model. If a board ever gets big enough for this to show, *that* is when to virtualize.
- **Dirty is measured against the serialized form**, not an edit counter — so an edit and its undo leave
  the document clean, which is what a person means by "I haven't changed anything".
- **`Pivotask.Core.Model.Label` collides with `Avalonia.Controls.Label`** in any file that uses both.
  Qualify one.
