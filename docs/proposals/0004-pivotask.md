# Proposal 0004 — **Pivotask**, a task-board editor for the Puffinote family

- **Status:** Draft — no phase started. **All §10 decisions are settled**; sequencing against proposal
  0003 is open by choice (§10.8 — the two are independent).
- **Author:** (you) · drafted with Claude
- **Affects:** new `features/ManageTasks` (Pivotask), `app/` (Puffinote shell — a fourth pane type),
  `shared/` (small additions to `Puffinote.Components`), `build/`, `justfile`
- **Prototype:** [`pivotask-prototype/pivotask-prototype.html`](../../pivotask-prototype/pivotask-prototype.html)
  (1,939 lines, self-contained) plus four screenshots. It is the behavioural spec for Phases 2 and 5.

---

## 1. Summary

Add **Pivotask** — a Trello-inspired task board — as the family's **fourth standalone editor**, composed by
Puffinote like the other three. One board lives in one JSON file, recognised by the compound extension
**`.pivotask.json`**.

Pivotask is the family's **first non-text editor**, and that changes three things:

1. **It shares almost nothing with `Puffinote.Text`** (proposal 0003). No piece table, no grapheme
   navigation, no encoding round-trip. Its document is a small object graph, not a byte buffer. What it
   *does* share is `Puffinote.Components` (theming, icons, overlays, commands, settings) and
   `Puffinote.Components.Core` (CLI scaffolding) — which is exactly what those libraries are for.
2. **It should not be custom-drawn.** Typage and Cellestra draw their own surfaces because no stock
   control can be a live-preview text editor or a spreadsheet grid. A board is a layout of discrete
   widgets — stock Avalonia controls are the right tool. See §5; this is a deliberate divergence and needs
   recording so nobody "fixes" it later.
3. **The CLI is genuinely useful**, not a testability afterthought. Task management is scriptable in a way
   text editing isn't, so `ptask` lands *before* the GUI (Phase 1), with a full mutating verb set, and
   shapes the core's API while it is still cheap to change.

Six phases, each ending with every area green and something runnable.

---

## 2. What the prototype already decides

The prototype is a working app with a settled data model. Reading it as the spec rather than a sketch:

**Document model** (`seed()`, prototype:1245–1295):

```
Board
└── groups[]        { id, name, collapsed, foldDone, tasks[] }
    └── tasks[]     { id, title, notes, due, important, done, steps[], labels[] }
        └── steps[] { id, text, done }
```

Labels are a flat vocabulary — `{ id, name, color }` — currently a **hardcoded constant**
(prototype:1229). In a real file format they must live in the document (§4).

**Interactions implemented** (from the action table and handlers): add/rename/delete group, collapse
group, fold completed, add task, toggle done, toggle starred, drag a task between and within groups,
open a task dialog (notes, steps with a progress bar, label picker, due date), delete task, delete all
steps, plus a progressive-disclosure "+" menu that reveals Labels / Due date / Steps only when used.

**Two views** — Board (columns) and List (grouped rows) — sharing one filter set (**All / Starred / Due
soon / Unfinished**) and one search box that matches title, notes, and step text.

**Dates are date-only ISO strings** (`YYYY-MM-DD`), rendered relatively ("Tomorrow", "1 day late",
"Thu, Jul 30"). No times, no recurrence, no timezone handling. Keep it that way for 1.0.

What the prototype has **no** answer for, and this proposal must supply: undo/redo, save semantics,
schema versioning, forward compatibility, external-edit reload, and multi-window/multi-pane behaviour.

---

## 3. Why a separate app

Same reasoning as proposals 0002/0003, and it applies more cleanly here than it did to Plaintain: a task
board is not a text document by any reading, so there is no "make it a Typage mode" temptation to resolve.
Pivotask is a peer editor — standalone `.app` and CLI, embeddable control, composed by Puffinote.

The one genuinely new question — whether a *notebook* app should treat task boards as a first-class
concept, with an aggregated "all tasks across the notebook" view — is **settled as no** (§10.7). A
`.pivotask.json` is a file that opens in a tab, exactly as `.md` and `.ods` do, and the shell keeps
delegating to editors rather than reading document contents itself. That closes what was this proposal's
largest scope risk; if the cross-file view is ever wanted, it is its own proposal against the shell.

---

## 4. The file format

**Extension:** `.pivotask.json`. The `.json` tail is deliberate — the file stays readable, diffable, and
openable by anything, and Plaintain (0003) can open it as text without ceremony.

**Two things the compound extension breaks**, both verified:

- **`EditorPaneFactory` cannot match it.** `Path.GetExtension("board.pivotask.json")` returns `".json"`,
  so the existing `HasExtension` helper is structurally unable to see it. The factory needs suffix
  matching (`path.EndsWith(".pivotask.json", OrdinalIgnoreCase)`) as a distinct concept from extension
  matching — and it must be checked *before* any generic `.json` handling, or Plaintain's text fallback
  (0003 §Phase 4) will swallow it.
- **Finder claims are not straightforward.** `CFBundleTypeExtensions` matches the final component, so
  claiming `pivotask.json` needs an **exported custom UTI** (`com.pivotask.board`, conforming to
  `public.json`, with a `UTTypeTagSpecification` filename-extension of `pivotask.json`). macOS does
  support multi-part extension tags — `.tar.gz` works this way — but this **must be verified on a real
  Mac before Phase 3 is called done**. If it doesn't hold, the settled fallback (§10.3) is to **ship no
  Finder claim at all** and open via the app, the CLI, and Puffinote. Do not claim bare `json` at any
  rank — not Owner, not Alternate.

**Serialization rules** (these are the format's real design, and they matter because this file will live
in git):

- `System.Text.Json` only — BCL, so no new dependency and no allowlist change.
- **Deterministic output**: stable key order, 2-space indent, LF endings, trailing newline. A save that
  changes nothing must produce a byte-identical file, so it never shows up in `git diff`.
- **Preserve unknown fields.** A file written by a newer Pivotask, opened and saved by an older one, must
  not lose data. Round-trip unrecognised properties on every object.
- **`"version": 1`** at the root, with a documented migration path. Reject a file whose major version is
  ahead rather than mangling it.
- **Atomic save** — temp file in the target's directory, then rename, matching the family's golden rule 5.
- **Labels move into the document**: a root-level `labels[]` of `{ id, name, color }`, referenced by id
  from tasks. Fixed palettes don't survive contact with real users.
- **One board per file** (§10.9). The root object *is* the board — no wrapping array, no board ids, no
  in-app board switcher. More boards means more files, which a notebook app already handles.

A JSON Schema for the format ships in `features/ManageTasks/docs/` and is what the tests validate against.

---

## 5. Architecture, and one deliberate divergence

Four projects, mirroring the family's shape:

| Project | Role |
|---|---|
| `Pivotask.Core` | The document model, JSON codec, mutations, command-based undo/redo, filter/search logic. **Zero UI.** Where most logic and tests live. |
| `Pivotask.Control` | Avalonia control library: `BoardView`, `ListView`, the task dialog, the label/due/step editors, and the one command catalog `TaskEditingCommands`. Embeddable unchanged into Puffinote. |
| `Pivotask.Desktop` | Standalone shell (assembly `Pivotask`): window, menu, open/save, recents, settings. Thin. |
| `Pivotask.Cli` | Second consumer of Core. No Avalonia. |

Dependency direction stays strictly **shell → control → core**, as elsewhere.

**The divergence: `Pivotask.Control` uses stock Avalonia controls, not a custom-drawn surface.**
Typage's golden rule 3 and Cellestra's equivalent exist because a live-preview text editor and a
spreadsheet grid are things no stock control can be. A Kanban board is `ItemsControl` + panels + Avalonia's
drag-and-drop — well-trodden ground where custom drawing would cost thousands of lines and buy nothing.
Recording it here so an agent reading a sibling `AGENTS.md` doesn't "correct" it. (Two places may still
want custom drawing if profiling demands it: the step progress bar and very large boards. Neither is a
Phase 2 commitment.)

**Undo/redo is command-based and Pivotask's own.** Proposal 0001 §3 already ruled that `EditHistory` is
engine-coupled and must not be shared; Cellestra's `IEditCommand` shape is the closer model. Session-only,
never written to the file, consistent with the family.

**Likely `shared/` additions**, promoted only when the duplication is real (0001 §3's rule of three):
a label/tag chip and a relative-date pill. Both plausibly belong in `Puffinote.Components` eventually;
neither is a Phase 0 commitment.

---

## 6. Phases

Each phase ends with `just check` green across every area, and lands as its own commit series on `main`.

### Phase 0 — `Pivotask.Core`: the format and the brain

New area `features/ManageTasks/` with `Pivotask.slnx`, its own `Directory.Build.targets` allowlist, and
the per-area docs every feature here carries (`AGENTS.md`, `README.md`, `docs/PRD.md`,
`docs/ARCHITECTURE.md`) — plus `docs/pivotask.schema.json`.

`Pivotask.Core` ships the model, the codec, and every mutation the prototype performs, each as an
undoable command. Filter and search logic lives here too (it is pure, and the CLI needs it as much as the
GUI does).

**Done when:** `Pivotask.Core.Tests` covers round-trip determinism, unknown-field preservation, version
rejection, every mutation, and undo/redo — and the prototype's seed data loads, saves, and compares equal.

### Phase 1 — the `ptask` CLI

Full mutating verb set (§10.5), binary named `ptask` (§10.2):

```
ptask list [--group G] [--label L] [--starred] [--due-soon] [--open] [--json]
ptask show <task>
ptask add "title" [--group G] [--label L] [--due DATE] [--star]
ptask done <task> | ptask undone <task>
ptask move <task> --group G [--before <task>]
ptask groups
```

Note the name diverges from the family's spell-it-out convention (`typage`, `cellestra`, `puffinote`) —
deliberately, because a task CLI is typed far more often than an editor's. It joins Plaintain's `pltn` as
the second short name; the assembly and app stay **Pivotask**.

Every one is a `Pivotask.Core` call, fully testable in the Linux devcontainer. Landing this before the GUI
means the core's API gets exercised by a real consumer while it is still cheap to change.

**Done when:** `Pivotask.Cli.Tests` drives the whole verb set end-to-end in-process.

### Phase 2 — `Pivotask.Control`: the board

`BoardView` and the task dialog, to the prototype's behaviour: columns, cards with label chips / due
pills / step counts / star, add-a-task inline, group collapse and fold-done, the progressive-disclosure
"+" menu, and drag-and-drop between and within groups. Plus `TaskEditingCommands` — **every command that
acts on the document lives here, never in a shell**, which is what keeps the standalone app and the
Puffinote pane identical.

List view, filters, and search are Phase 5 (§10.6 may move them here).

**Done when:** `Pivotask.UiTests` (headless) drives add, complete, star, reorder, cross-group move, and
the full task dialog.

### Phase 3 — `Pivotask.Desktop` + packaging → **first user-visible milestone**

Thin shell: window, menu, status bar, open/save dialogs, recent files, settings, and `args[0]` open on
launch mirroring `Typage.Desktop/App.cs`. New-file creation matters more here than for a text editor —
"new board" needs a sensible starting set of groups.

Packaging lands in the same phase:

- `build/package-mac.sh` — one new `case` arm. Do not fork the script.
- `Pivotask.Desktop/Info.plist` — bundle id `com.pivotask.Pivotask`, the exported UTI of §4, and
  `LSHandlerRank: Owner` for it. **Verify the compound-extension claim on a real Mac early in this
  phase**, not at the end — if it fails, §10.3 says ship the bundle with no document-type claim, and the
  phase is still done.
- `justfile` — the full recipe set for a fourth area, verb-first, every install with its exact inverse
  (including the `lsregister -u` withdrawal), plus `pivotask` added to all nine aggregate recipes. Note
  the recipes are named for the area (`install-pivotask`) while the binary they install is `ptask`.
- Root `AGENTS.md` — repo map and area table grow a row.

**Done when:** double-clicking a `.pivotask.json` in Finder opens Pivotask.

### Phase 4 — Puffinote integration

`TaskBoardPane : IEditorPane` in `app/Puffinote.Desktop/Panes/`, a thin adapter over `BoardView` —
composition, never a fork, as `MarkdownPane` wraps Typage.

Two shell-side changes worth calling out:

- **`EditorPaneFactory` learns suffix matching** (§4), ordered ahead of any generic `.json` route.
- **`GetOutline()` returns the group list** — `OutlineItem(name, level: 0, Reveal)` per group, scrolling
  the board to that column. Unlike Plaintain (0003 §9.4), Pivotask has real structure to contribute, and
  it maps onto the existing outline contract without changing it.

External edits matter more than for the other editors — a board file is small, hand-editable, and likely
to arrive via `git pull`. Wire `NotebookWatcher` to a reload path, with unsaved-changes handling.

**Done when:** a `.pivotask.json` in a notebook opens as a tab, the sidebar outlines its groups, and an
out-of-band edit reloads cleanly.

### Phase 5 — List view, filters, search, and polish

The remainder of the prototype's surface: `ListView`, the All / Starred / Due soon / Unfinished filter
set, the search box (title + notes + step text), relative date rendering, keyboard navigation and
shortcuts, and drag-and-drop refinement. Then the §5 re-evaluation — promote the chip and date-pill to
`Puffinote.Components` only if the duplication is real.

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| **The compound-extension Finder claim doesn't work** | Verify on a real Mac early in Phase 3. The fallback is settled (§10.3): ship no document-type claim, and Pivotask opens via the app, `ptask`, and Puffinote. Not a blocker for any phase. |
| **A `.json` route added for Plaintain swallows `.pivotask.json`** | Ordering is explicit in §4 and Phase 4. Add a regression test in `Puffinote.Tests` that asserts the routing, so 0003's Phase 4 can't silently break it. |
| **Drag-and-drop is the fiddliest part and lands mid-plan** | Basic DnD in Phase 2, polish in Phase 5. Keep a keyboard/menu path to "move task" so the feature is never blocked on DnD feeling right. |
| **Scope creep toward a project manager** | Assignees, recurrence, times, dependencies, and multi-board are all out for 1.0. §10.7 is the one open door — decide it deliberately. |
| **Fifth area multiplies ceremony** (~15 justfile recipes, plist, four docs) | Accepted; it is the price of the architecture. Note this is the *second* new area proposed — 0003 and 0004 together take the repo from three to five. Sequencing them is §10.8. |
| **Stock-controls divergence gets "corrected"** | Recorded in §5, and to be restated in Pivotask's own `AGENTS.md` and `ARCHITECTURE.md`. |

---

## 8. Explicit non-goals for 1.0

No assignees or multi-user anything. No recurring tasks. No due *times* or timezones — dates only. No
task dependencies, subtask nesting beyond the one `steps[]` level, or Gantt/calendar views. No sync, no
server, no notifications. No attachments. No plugin system. No new NuGet above the trusted base.

Also settled in §10 and worth stating as boundaries: **task notes are plain text** — no Markdown
rendering, no embedded editor; **one board per file**; and **no cross-notebook task aggregation** in the
shell.

---

## 9. What this shares with proposal 0003

Almost nothing, and that is the useful finding. Pivotask does not consume `Puffinote.Text`; Plaintain does
not consume `Pivotask.Core`. The two proposals touch the same files in exactly two places — the `justfile`
aggregate recipes and `EditorPaneFactory` — and §7 records the one real interaction (JSON routing order).
They can proceed independently, in either order.

---

## 10. Resolved decisions

Settled at drafting time. Treat these as firm — reopening one needs a reason recorded here, not a
drive-by change. (Same convention as Typage PRD §12 and proposal 0003 §9.)

**10.1 — Feature folder is `features/ManageTasks/`.** Breaks the literal `Edit*` prefix the other three
areas share, deliberately: the folder names are verb-noun, and "manage" is the honest verb for a board
app. Used throughout this document.

**10.2 — The CLI binary is `ptask`.** Diverges from the family's spell-it-out convention
(`typage`/`cellestra`/`puffinote`) because a task CLI is typed many times a day and an editor's is not.
It joins Plaintain's `pltn` as the second short name — two exceptions now, so treat "short name when the
tool is used constantly" as the emerging rule rather than an accident. **The app, assembly, bundle, and
justfile recipes stay `Pivotask`/`pivotask`**; only the binary is short.

**10.3 — If the macOS compound-extension UTI fails, ship no document-type claim.** Pivotask then opens
via the app, `ptask`, and Puffinote. Bare `json` is not claimed at any rank — putting Pivotask in the
"Open With" menu of every JSON file on the machine is noise for files it cannot parse. The extension
stays `.pivotask.json` either way; the "it's just JSON" property is worth more than the Finder claim.

**10.4 — Task notes are plain multi-line text.** No Markdown rendering, no embedded `MarkdownEditorView`.
A note attached to a task is a sentence or two, not a document — and the alternatives cost either a
second Markdown implementation or a feature→feature dependency that would break the editors-are-
independent property. If a task's notes genuinely want to be a document, that document is a `.md` file.

**10.5 — The CLI gets the full mutating verb set.** `list`/`show`/`add`/`done`/`undone`/`move`/`groups`.
A task CLI that can't add a task isn't worth shipping, and every verb is a `Pivotask.Core` call that
needs test coverage regardless of who calls it.

**10.6 — Board view ships first; List view is Phase 5.** Gets a usable app sooner, and List is the
cheaper of the two once card rendering exists. Consequence: design the filter/search seam in Phase 2
knowing a second view will consume it, so Phase 5 is an addition rather than a rework.

**10.7 — Puffinote does not aggregate tasks across the notebook.** A `.pivotask.json` opens in a tab like
any other file. Keeps the shell's delegation property intact — Puffinote has never read a document type's
contents itself, and this is not the feature to break that for. A cross-notebook task view remains
possible later as its own proposal against the shell, not as a clause in this one.

**10.8 — Sequencing against proposal 0003 is free.** The two proposals touch in exactly two places
(§9), so neither blocks the other; pick by appetite. The one ordering constraint that *does* exist is
internal to the interaction: whichever lands second must not break the JSON routing order of §4, which
the regression test in §7 exists to enforce.

**10.9 — One board per file.** The root JSON object *is* the board — no wrapping array, no board ids, no
in-app board switcher. Matches the prototype's model exactly and keeps the schema flat; "more boards"
means more files, which a notebook app already handles natively.
