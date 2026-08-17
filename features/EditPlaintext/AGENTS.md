# AGENTS.md

Guidance for an AI coding agent (and any human) working in the **Plaintain** feature
(`features/EditPlaintext/`). Read the repo-wide [`AGENTS.md`](../../AGENTS.md) first (build/test/format
gates, git workflow, the one-way layering rule), then this, then the linked docs. This mirrors the
conventions of the sibling features ([Typage](../EditMarkdown/AGENTS.md),
[Cellestra](../EditSpreadsheets/AGENTS.md)) — same repo, same tooling, same golden-rule discipline.

## What this project is

**Plaintain** (**plain** + **plantain**) is a Notepad-class plaintext editor: the tool you reach for
when a file is "just text" and a Markdown editor would only get in the way. It opens *any* decodable
file, claims `.txt` and friends in Finder, and its CLI is **`plaintain`**.

It is the family's third standalone editor, and — like the other two — Puffinote composes it. Puffinote
embeds `Plaintain.Control` **unchanged**, so anything that acts on the open document belongs in that
project, not in `Plaintain.Desktop`, or the standalone app and the Puffinote pane become two editors.

Start with [docs/PRD.md](docs/PRD.md) (what and why) and
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (how). The design rationale lives in
[proposal 0003](../../docs/proposals/0003-plaintain.md), which is still the reference for *why* things
are shaped this way. When this file and a doc disagree, the doc wins for design detail; this file wins
for process.

## Golden rules (non-negotiable)

1. **The surface is Plaintain's own, and the semantics are shared.** Grapheme boundaries, selection,
   undo, search, stats, and the file codec come from `shared/Puffinote.Text` — the same code Typage
   uses, so a caret cannot behave differently in the two editors. The *geometry* is ours and is
   deliberately not shared: see rule 2.
2. **Uniform rows, and the arithmetic that follows.** Every row is the same height and every character
   the same width, because the font is monospace by design. That is what makes a line's row count
   `ceil(columns / columnsPerRow)` rather than a measurement, and it is the entire reason a 50 MB log
   is viable. Do not introduce proportional fonts, variable row heights, or per-line measurement into
   the layout path without reopening PRD §3.
3. **No format-aware rendering, ever** (proposal 0003 §9.2). No CSV-as-table, no JSON folding, no
   syntax highlighting. A delimited file is Cellestra's question; structured config is still text. This
   is the line that keeps Plaintain from drifting into a code editor, and every "small" exception to it
   reopens the case for the app existing at all.
4. **Every command that acts on the document lives in `Plaintain.Control`** (`PlainTextEditingCommands`).
   A shell contributes the window, the file dialogs, recent files, settings — nothing else.
5. **Byte-for-byte file handling.** Encoding and line endings are preserved exactly; a file whose bytes
   are not valid UTF-8 opens **read-only** rather than being rewritten through a guess. See PRD §4.
6. **Guard the supply chain.** Trusted base = .NET BCL + first-party Avalonia. New packages go through
   `Directory.Build.targets`' allowlist with a stated reason. Never npm.

## Where things go

- **Anything true of text regardless of format** → `shared/Puffinote.Text/`. If a change would read the
  same in Typage, it belongs there.
- **The surface, and every command that acts on the document** → `Plaintain.Control/`
  (`Controls/`, `Views/`, `Commands/`). Puffinote embeds this.
- **Shell** → `Plaintain.Desktop/` — window, menu, file dialogs, recents, settings, status bar.
- **CLI** → `Plaintain.Cli/` (`plaintain`). A launcher plus one read-only verb; it does not grow a verb set.
- **Tests** → `Plaintain.UiTests/` (headless Avalonia — the gate, since the container can't launch a
  GUI) and `Plaintain.Cli.Tests/`.

## Everyday commands

```bash
just build-plaintain              # = dotnet build features/EditPlaintext/Plaintain.slnx
just test-plaintain
just test-filter SomeTest plaintain
just run-plaintain-cli info server.log
just check                        # format-verify + build + test EVERY area — the gate
```

**Definition of done:** `just check` passes. Warnings are errors (`TreatWarningsAsErrors=true`), so
"builds" means zero warnings. Touching `shared/` means every area is affected — never only
`just test-plaintain`.

**Verifying changes.** The devcontainer is headless: `just run-plaintain` fails environmentally, and
that is not a regression. Verify through `Plaintain.UiTests` or `plaintain info`.

## Working in this codebase (gotchas)

- **The surface is one control across `PlainTextEditor.*.cs` partials** (`.Caret`, `.Input`, `.Pointer`,
  `.Rendering`, `.Clipboard`). It implements `IScrollable`, so a host `ScrollViewer` hands it an offset
  instead of arranging a million rows — which means the control is always viewport-sized and
  `Extent` is where the document's real size lives. Anything that grows the control's own size breaks
  virtualization silently: the tests will pass and a large file will freeze.
- **Columns are expanded columns.** A tab advances to the next 4-stop, so `ColumnOf` / `OffsetAtColumn`
  are not string indices. Mixing the two is the easiest bug to write here.
- **`FormattedText` has no tab stops**, which is why the renderer expands tabs to spaces before drawing.
  Draw a literal tab and every column under it is wrong.
