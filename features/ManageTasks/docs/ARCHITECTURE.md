# Pivotask — Architecture

How the task board is put together. What it does and why is [PRD.md](PRD.md); the design rationale is
[proposal 0004](../../../docs/proposals/0004-pivotask.md).

## 1. Projects

| Project | Role |
|---|---|
| `Pivotask.Core` | The model, the JSON codec, every mutation as an undoable command, filter/search. **Zero UI.** Where most logic and tests live. |
| `Pivotask.Control` | The Avalonia control library: `TaskBoardView` (the embeddable composite), `BoardView`, `TaskListView`, `TaskCard`, `TaskDialog`, and the one catalog `TaskEditingCommands`. **This is what Puffinote embeds.** |
| `Pivotask.Desktop` | The standalone shell (assembly `Pivotask`): window, menu, file dialogs, status bar. Thin. |
| `Pivotask.Cli` | `ptask` — a full mutating verb set. No Avalonia. |
| `Pivotask.Core.Tests` / `Pivotask.UiTests` / `Pivotask.Cli.Tests` | |

Dependency direction is strictly **shell → control → core**. Nothing references Puffinote.

## 2. The deliberate divergence

**`Pivotask.Control` uses stock Avalonia controls, not a custom-drawn surface.** Typage's and Cellestra's
golden rules mandate custom drawing because a live-preview text editor and a spreadsheet grid are things
no stock control can be. A Kanban board is not one of those: it is `ItemsControl` + panels +
drag-and-drop, well-trodden ground where custom drawing would cost thousands of lines and buy nothing.

This is recorded in three places — here, in `AGENTS.md`, and in the control's `.csproj` — so an agent
reading a sibling area's guide doesn't "fix" it.

## 3. Core

### 3.1 The document

`BoardDocument` is the single seam every consumer edits through: the board, its `BoardHistory`, its path,
and whether it is dirty. The GUI, the CLI, and the Puffinote pane all hold one, so "how do you change a
board" has one answer and it is always undoable.

**Dirty is measured against the serialized form**, not an edit counter. An edit and its undo therefore
leave the document clean, which is what a person means by "I haven't changed anything".

### 3.2 Commands

`IBoardCommand` is apply/revert, and `BoardCommands` is a factory per mutation. Two are deliberately
coarse rather than composed: `MoveTask` is one command (not a delete plus an add) and `ClearSteps` is one
command (not N deletes), because undo should cost the person who made the mistake exactly one press.

Undo/redo is Pivotask's own. Proposal 0001 §3 ruled that Typage's `EditHistory` is engine-coupled and
must not be shared; Cellestra's `IEditCommand` shape is the closer model, and this follows it.

### 3.3 The codec

`BoardCodec` is hand-written over `System.Text.Json`, not attribute-driven, because the two requirements
— round-tripping unrecognised properties and a fixed key order — are precisely the things a serializer
does not do for you. Each model object carries an `Unknown` dictionary; the writer emits it last, ordered,
so an unknown field lands in the same place every time and the file does not churn.

## 4. The control

`TaskBoardView` is the composite: the filter/search strip, the board or list under it, the task dialog
over it, and the catalog. `BoardView` and `TaskListView` both read the document and both consume the same
filter/search seam from `BoardQuery` — which is why Phase 5's list view was an addition rather than a
rework.

**Rebuild, don't diff.** Both views rebuild wholesale when the document changes. A board is tens of
tasks, so a rebuild is cheap, and it removes an entire class of bug: a view that has quietly stopped
agreeing with the model. If a board ever gets big enough for this to show, that is the moment to
virtualize, and not before.

## 5. Settings

`Pivotask.Desktop/Services/SettingsService.cs` — an `AppSettings` of **two** fields (the board's default
layout and default filter), persisted as JSON under the OS per-user app-config path through the shared
tolerant `SettingsStore`. File ▸ Preferences… (⌘,) edits them via the shared `SettingsEditorView`.

It is two fields on purpose. Everything else about a board — its groups, labels, ordering — lives *in the
board file*, where it travels with the document and is shared by everyone who opens it. Only a standing
personal preference about how *you* read a board belongs in a per-user config, and there are exactly two
of those. Nothing about the window (its size, its position) is here either: that's session state, not a
preference, and inventing settings to fill out a dialog is how a config file stops meaning anything.

## 6. The Puffinote seam

`TaskBoardPane` wraps `TaskBoardView`, and `EditorPaneFactory` routes to it — **by suffix, ahead of every
extension test**. `Path.GetExtension("board.pivotask.json")` — the name boards had before the rename —
returns `".json"`, which is on Plaintain's
fast list, so a board matched by extension would open as a wall of JSON. There is a regression test for
this in `Puffinote.UiTests`, which is what proposal 0004 §7 asks for.

`GetOutline()` returns the group list — unlike Plaintain, which returns nothing permanently, a board has
real structure to lend, and it maps onto the existing outline contract without changing it.

## 7. Packaging

`build/package-mac.sh pivotask` builds `Pivotask.app` from `Pivotask.Desktop/Info.plist`. The compound
extension needs an **exported custom UTI** (`com.pivotask.board`, conforming to `public.json`, with a
two-part filename-extension tag). macOS does support this — `.tar.gz` works the same way — but **it must
be verified on a real Mac**. If it doesn't hold, §10.3 settles the fallback: ship with no document-type
claim at all, and Pivotask opens via the app, `ptask`, and Puffinote. Bare `json` is not claimed at any
rank.
