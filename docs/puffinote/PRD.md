# PRD — Puffinote, a second-brain notes app

> **Working name:** **Puffinote** (puffin + note)
> **Document status:** Draft v1
> **Related docs:** [Proposal 0001 — `Puffinote.Components`](../proposals/0001-puffinote-components.md) ·
> [Typage PRD](../../features/EditMarkdown/docs/PRD.md) · [Cellestra PRD](../../features/EditSpreadsheets/docs/PRD.md)

---

## 1. Summary

**Puffinote** is a desktop **personal-knowledge-management** app in the spirit of Obsidian: a classic
**sidebar-and-editor** workspace with a **file tree** on the left and a rich editing surface on the
right. It does not invent new editors — it **composes** the two editors this repo already builds:

- **Markdown (`.md`)** files edited through **Typage**, the from-scratch WYSIWYG live-preview Markdown
  editor (`features/EditMarkdown`).
- **Spreadsheet (`.ods`)** files edited through **Cellestra**, the from-scratch OpenDocument spreadsheet
  editor (`features/EditSpreadsheets`).
- **Future interfaces** (canvas, kanban, databases, PDF, …) are anticipated by the architecture but are
  **out of scope for 1.0**.

Puffinote is the composite third app of the **Puffinote family** (Proposal 0001): Cellestra and Typage
remain independently shippable, and all three stand on the shared **`Puffinote.Components`** app-shell /
design-system library. Puffinote adds the shell that a notebook, a file tree, tabs, a merged command
palette, a unified CLI, and a shared shortcuts editor require — **without forking either editor**.

Like its siblings, Puffinote is built on **Avalonia UI / .NET 10**, keeps a **minimal, auditable
dependency surface** (.NET BCL + Avalonia + OS system services; no new NuGet packages, no npm), and is
evaluated against **Nielsen's 10 usability heuristics** (§6).

---

## 2. Goals and non-goals

### 2.1 Goals

- A **single-notebook**, sidebar-and-editor PKM workspace that feels calm, fast, and keyboard-first.
- **Compose, don't fork:** embed the *existing* Typage and Cellestra editing surfaces unchanged; new
  file interfaces can be added by dropping in a control, not rewriting the shell.
- A **collapsible sidebar** that is fully **focusable, navigable, and operable by keyboard**.
- **Tabs:** multiple documents open at once, freely mixing notes and sheets, each with its own
  dirty/undo state.
- **One merged Command Menu** that fuses Puffinote's shell commands with the active editor's commands.
- A **unified CLI** (`puffinote`) that routes to the sub-app command lists plus notebook-level commands, so
  terminal users and AI agents drive the whole notebook from one entry point.
- A **shared keyboard-shortcuts editor** whose bindings work identically in Puffinote *and* in each
  separately-exported app.
- **Configuration that inherits** each sub-app's own settings as the base and layers Puffinote overrides
  on top (§7.7).
- **Graceful shortcut-conflict handling:** a friendly launch warning when a sub-app's configured
  shortcut collides with a Puffinote reserved key, plus a deterministic runtime resolution rule (§7.6).
- Preserve every sub-app guarantee: **no data loss**, byte-faithful save, `.ods`/`.md` correctness.

### 2.2 Non-goals (Puffinote 1.0)

- **Multi-root workspaces.** One notebook (one root folder) is open at a time (§7.1). Attaching several
  arbitrary folders at once is a later item.
- **Split panes / grid layouts.** 1.0 ships **tabs**, not side-by-side splits (a note beside a sheet).
  The tab model is designed so splits can be added later without a rewrite.
- **New editors.** No canvas, kanban, database, or PDF *editor* in 1.0. Non-openable files get preview /
  open-externally only (§7.3).
- **Backlinks, graph view, wiki-link resolution, tags index, transclusion/embeds across files.** Typage
  *recognizes* wiki-links/embeds but they are off by default; Puffinote does not add a link graph in 1.0.
- **Plugins / third-party extension marketplace.**
- **Sync, accounts, collaboration, comments, track-changes, cloud.** The notebook is local files.
- **A new document engine.** All Markdown logic stays in `Typage.Engine`; all spreadsheet logic stays in
  `Cellestra.Core`. Puffinote owns *shell*, never *domain* (Proposal 0001 §4).
- **Cross-platform 1.0.** Puffinote 1.0 is **macOS-first** (§5), because Cellestra is macOS-only today.

---

## 3. Background & the Puffinote family

This app is the long-stated destination of the family. Typage's own PRD calls itself "the first
deliverable of a larger Obsidian-class notes app," and Proposal 0001 stood up `Puffinote.Components`
precisely so a third, composite app could reuse the shell:

```
                 ┌──────────────────────────────┐
                 │      Puffinote.Components      │   (Proposal 0001 — landed, Phases 0–5)
                 │  (design system + app shell)   │
                 └──────────────────────────────┘
                    ▲           ▲            ▲
        ┌───────────┘           │            └────────────┐
   Cellestra (standalone)   Typage (standalone)      Puffinote (THIS DOC)
   sheets                   notes                    notes + sheets, embeds both
```

Three constraints carry over from the siblings and drive Puffinote's design:

1. **Supply-chain control.** The trusted base is **.NET BCL + Avalonia (+ native deps) + OS system
   services**. No Markdown/editor/PDF library, no npm. (Typage AGENTS §"Golden rules"; Cellestra Golden
   Rule #6.)
2. **Reusability / layering.** Dependency direction is strictly one-way. Puffinote references the
   editors and the shared library; **nothing references Puffinote**. The shared library may not reference
   any app (Proposal 0001 §4.4).
3. **Usability.** As a Writer/Numbers-class tool it must meet document-editor expectations, evaluated
   against Nielsen's heuristics (§6).

**What is already built** (so 1.0 is mostly *shell assembly*, not new engines): the `Puffinote.Components`
library ships the shared `AppCommand`/`Shortcut`/`ShortcutFormatter`, `FuzzyMatcher`, the
**command-palette** control (`PaletteListView`), the **overlay/modal host** (`OverlayHost` + Tab
focus-trap + `Confirm`/`Choice`/`Message`/`Form` dialogs), the **icon system** (Lucide), the
**tooltip card**, the **theming token contract** (`ThemePalette`/`IAppTheme`/`PuffinoteTheme`/`Tokens`),
and shared **shell services** (`SettingsStore`, `RecentFiles`, and the non-UI `Puffinote.Components.Core`
CLI harness `CliHost`/`CliException`). Both editors already run their palette and modals through these.

---

## 4. Target users and use cases

| User | Primary use case |
|---|---|
| Note-takers / "second brain" users | Keep a notebook of Markdown notes; jump between them from the tree; write with formatting visible inline. |
| Analysts / planners | Keep budgets, trackers, and tables as `.ods` alongside notes; open a sheet in a tab next to a note. |
| Keyboard-first / power users | Never touch the mouse: Command Menu, sidebar keyboard nav, custom shortcuts. |
| Terminal users & AI agents | Drive notebook contents from the `puffinote` CLI (list, search, read, edit notes and sheets). |
| Existing Typage / Cellestra users | Keep using the standalone apps; Puffinote is additive, not a replacement. |

**Core journey:** open a notebook (folder) → the file tree renders → open a `.md` or `.ods` file into a
**tab** → edit with the full native editor for that type → run any action from the **merged palette**
(⌘K) → save (atomic, byte-faithful) → close the tab (prompt if unsaved).

---

## 5. Product principles and constraints

- **Compose, don't fork.** Puffinote embeds `Typage.Control`'s `MarkdownEditor` and Cellestra's
  spreadsheet grid **unchanged**. Any behavior a sub-app owns stays owned by the sub-app; Puffinote adds
  only the surrounding shell. If embedding needs a seam, the seam is added to the *editor* or the *shared
  library*, never a special-case in Puffinote reaching into an editor's internals.
- **Single notebook, single window.** One root folder, one window (multiple tabs). Opening another notebook
  reuses the window and prompts to save unsaved tabs.
- **Keyboard-first & accessible.** The sidebar and every command are reachable and operable from the
  keyboard; new controls expose automation peers (carried from both AGENTS files).
- **Minimal dependencies, enforced.** The sub-apps' dependency-allowlist CI gate extends to Puffinote —
  the build fails on any NuGet dependency outside the allowlist.
- **Data integrity over features.** Every path that risks user data is atomic, recoverable, and
  non-destructive — inherited from Typage §7.1 and Cellestra §8.
- **macOS-first (1.0).** Cellestra is macOS-only today, so Puffinote 1.0 targets macOS (native menu bar,
  ⌘ key-equivalents). The shell and Typage are already cross-platform; broad cross-platform Puffinote is
  a **future goal**, gated on Cellestra going cross-platform (called out as a risk in §12).
- **One command catalog per surface; merged for display.** Each app keeps its *own* catalog (Cellestra
  Golden Rule #4; Typage's single command catalog). Puffinote does not fork them — it **merges** its own
  shell catalog with the active editor's catalog at runtime (§7.4).

---

## 6. Usability principles (Nielsen's 10 heuristics)

| # | Heuristic | How Puffinote satisfies it |
|---|---|---|
| 1 | **Visibility of system status** | Sidebar shows the notebook name + current file; each tab shows its dirty dot; status bar shows the active editor's own status line (delegated); background notebook-index/search progress is shown, not blocking. |
| 2 | **Match to real world** | Familiar file-tree + tabs + editor layout; native macOS menu bar; plain-language labels; "Properties," "Sheet," "Note" wording from the sub-apps. |
| 3 | **User control & freedom** | Undo/redo delegated to the focused editor; collapsible sidebar; close/reopen tabs; Cancel on every dialog; the shortcut-conflict warning tells you exactly how to fix it (§7.6). |
| 4 | **Consistency & standards** | One design system (`Puffinote.Components` tokens); shortcuts render as ⌘ on macOS via the shared `ShortcutFormatter`; the same command runs identically from menu, palette, and keybinding. |
| 5 | **Error prevention** | Prompt on unsaved tab/notebook close; the launch **conflict-warning modal** prevents silently-shadowed shortcuts; sub-app save-safety (encoding guard, atomic write, `.ods` unmodeled-content preservation) is untouched. |
| 6 | **Recognition over recall** | Merged Command Menu exposes *every* action; sidebar panels (tree, search, outline, recent); tooltips show shortcuts; the shortcuts editor lists all bindings in one place. |
| 7 | **Flexibility & efficiency** | Full keyboard operation; customizable shortcuts; recent-files quick-open; collapsible chrome; the `puffinote` CLI for scripted/agent workflows. |
| 8 | **Aesthetic & minimalist design** | Calm single-notebook focus; collapsible sidebar; the editor is the hero; chrome reads one design language across notes and sheets. |
| 9 | **Recognize, diagnose, recover** | Errors state OS reason + suggested action (inherited from the sub-apps); a sub-app that fails to load a file surfaces its own error dialog through the shared `MessageDialog`. |
| 10 | **Help & documentation** | Help menu with the offline shortcut reference + each editor's syntax/function reference; the conflict modal links to the shortcuts editor. |

---

## 7. Functional requirements

### 7.1 The notebook

- **The Workspace Rail** runs down the far-left edge of the window: **Todos**, **Calendar**, **Notes**,
  **Data**, top to bottom, one glyph and one word each. These are different *things to be doing* rather
  than different views of one thing, which is why they get a destination each (Outlook's shape, and
  Outlook's reason for it) instead of a mode buried in a menu. The active one wears a leading-edge accent
  bar, a wash and a spoken "selected" — a shape as well as a colour, because colour alone is no signal to
  a reader who cannot see it. Every entry is also a command, so the Command Menu and the View menu reach
  all four without the pointer.
  - **Notes** is the notebook: the sidebar, the tabs, the editors — everything else in this document. It
    is the workspace the window opens in, and it is *kept alive* while you are elsewhere, because an open
    tab, a scroll position and a selection all live in those controls.
  - **Todos**, **Calendar** and **Data** are **not built**. Each shows its name, one sentence saying what
    will live there (every `.pivotask` board in the notebook; the notebook's dated work on a calendar;
    every `.ods` workbook in the notebook), and the words "Not built yet." A rail entry leading to a blank
    page reads as a broken app; saying what is coming, where it will be, is the honest version.
  - The rail is **not** the sidebar's panel switcher, and does not look like it. That one moves between
    panels *within* the notebook (Files, Search, Outline, Recent) and collapses to nothing; this one is
    always there, and carries a word under each glyph.
- A **notebook is a single root folder** on disk. Puffinote opens one notebook at a time; the file tree is
  rooted at the notebook folder. Switching notebooks (Open Notebook…) prompts to save any dirty tabs, then reloads
  the tree and per-notebook state.
- **Notebook-scoped state** (open tabs, last-active file, sidebar width/collapsed, per-notebook overrides) is
  persisted **inside the notebook** in a `.puffinote/` directory, so a notebook is self-describing and
  portable. Global/user preferences live in the user config dir (§7.7).
- **Version control:** for a notebook under git, Puffinote ships a `.puffinote/.gitignore` that **ignores the
  volatile state** (search index, window/tab/session state) but **allows the durable per-notebook settings**
  (theme, overrides) to be committed, so preferences travel with the repo while machine-local churn does
  not.
- **Recent notebooks** (MRU) are offered at startup and in the menu, reusing the shared `RecentFiles`
  mechanism from `Puffinote.Components.Shell`.
- **External changes** to files in the notebook are detected by the shell's own `NotebookWatcher`: an
  add/remove/rename refreshes the tree and the search index, and **every open tab is reconciled against
  the disk** in the same pass. A tab with no unsaved edits reloads — it has nothing to lose and stale
  content is the only thing it could otherwise show. A tab that *is* dirty is left alone and flagged;
  saving it then asks whether to overwrite the change on disk or reload and lose the edits, and auto-save
  stands down on it entirely rather than making that choice on a timer. **A reload is an undoable step**
  wherever the pane's editor has a history to keep it in (`IEditorPane.ReloadAsync`, which defaults to a
  plain re-open): taking somebody else's version is the change a reader most wants a way back from, so ⌘Z
  in the tab gives back what the tab was holding. (This was originally written as
  riding "the sub-apps' existing external-change flows", which an embedded editor does not have: the
  editors are loaded from a path by the shell, so noticing is the shell's job.)

### 7.2 The sidebar & file tree

- **Layout:** a left sidebar with a **file tree** rooted at the notebook, plus additional panels (§7.2.4).
  The main area is the **tab strip + active editor**.
- **File tree contents (all files shown):** the tree shows *every* file and folder in the notebook.
  - `.md` → opens in the **Typage** surface. `.ods` → opens in the **Cellestra** surface. **Anything else
    that reads as text** — a `.log`, a `.conf`, an extensionless `LICENSE` — opens in the **Plaintain**
    surface, decided by a probe over the file's first few KB rather than a list of extensions.
  - **Files no editor here can open** (images, PDFs, archives) are shown for context and are **actionable**:
    where feasible a lightweight **read-only preview** (e.g. images), otherwise **"Open in default app"**
    / **"Reveal in Finder."** No new editor is built for them (§2.2).
  - Standard tree affordances: expand/collapse folders, new file/folder, rename, delete (confirmed),
    reveal in Finder, and drag to move (move is a plain filesystem move; Puffinote does **not** rewrite
    link references — link-refactor is a non-goal).
  - **The move gesture** is drawn by the tree itself rather than handed to the platform's drag-and-drop,
    because all three of the things that make a tree drag legible are ours to draw: a **ghost** of the entry
    under the pointer, a **drop indicator** outlining where it would land, and a hovered collapsed folder
    **opening** after a second so you can aim inside it without letting go. A drop on a *folder* means that
    folder, on a *file* the folder holding it, and on the empty space below the rows the top level of
    whatever the tree is scoped to. Moves that would change nothing (into the folder it is already in) or
    swallow themselves (a folder into its own descendant) show no indicator and do nothing on release; a
    name already taken in the target is refused rather than overwritten. Escape abandons a drag in flight.
    There is no confirmation prompt: the gesture said what and where, it is visible throughout, and it is
    undone by dragging the entry back. Open tabs and the recent list are reconciled exactly as a rename
    reconciles them.

#### 7.2.1 Collapsible

- The sidebar is **collapsible** to a zero/handle width and back, via a toolbar control, a menu item, a
  command-palette action, and a keyboard shortcut. Collapsed state is persisted per notebook. Collapsing the
  sidebar moves focus to the active editor; expanding it can optionally restore focus to the tree.
- **The toolbar control is one affordance in two places.** Expanded, it leads the sidebar's own header;
  collapsed, the sidebar is zero-width and a narrow **handle** takes the column, carrying the same button in
  the same corner. Neither state can strand the user: the sidebar is always one click from where it was.
- **Touchpad gesture.** A two-finger horizontal swipe over the window also toggles it: **swipe left opens**
  the sidebar, **swipe right collapses** it. A swipe is only recognized when its horizontal travel clearly
  dominates the vertical and accumulates past a deliberate-gesture threshold, and it acts **once** per
  physical swipe. It never overrides a region that genuinely scrolls sideways (the tab strip, a wide table, a
  document with lines past the right edge) — those keep the horizontal axis.

#### 7.2.2 Focusable & keyboard-operable (hard requirement)

The sidebar must be **fully operable with the keyboard alone**:

- A shortcut **focuses the sidebar / file tree** (and one to focus the editor), so the user can move
  between panes without the mouse.
- Within the tree: **Up/Down** move the selection, **Left/Right** collapse/expand (or ascend/descend),
  **Home/End** jump to first/last, **type-to-search** jumps to a matching name, **Enter** opens the
  selected file into a tab (or toggles a folder), and context actions (rename `F2`, delete, new file) are
  keyboard-bound.
- The tree exposes **automation peers** (roles/labels) and a visible focus ring; concealed/decorative
  elements never remove content from the accessibility tree (carried from both AGENTS files).

#### 7.2.3 Search & outline & recent (sidebar panels, 1.0)

Alongside the file tree, 1.0 ships three panels (switchable, each keyboard-focusable):

- **Notebook-wide search** — full-text search across the notebook. Notes search the raw Markdown; sheets search
  cell **contents** (text/values) where feasible via `Cellestra.Core`/the CLI engine. Results are a
  keyboard-navigable list that opens the hit in a tab. Indexing runs in the background (status shown, non
  -blocking).
- **Document outline** — a structural view of the **active** document: Markdown **heading outline** for
  notes (Typage already computes this — see `typage outline`), and the **sheet + chart list** for
  workbooks (Cellestra already exposes sheets/charts). Selecting an entry navigates within the editor.
- **Recent files** — a notebook-scoped recent list, reusing shared `RecentFiles`.

#### 7.2.5 Quick-open (⌘O)

A dedicated **fuzzy quick-open** (⌘O), separate from the Command Menu, jumps to any file in the notebook
by typing part of its name. It reuses the shared `FuzzyMatcher` and the `PaletteListView` presentation,
ranks by name match + recency, and opens the chosen file into a tab (Enter) — the signature keyboard-first
navigation PKM users expect. (The Command Menu stays commands-only; file navigation lives here.)

### 7.3 The editor host (tabs + embedding)

- **Tabs:** the main area is a **tab strip** over a stack of live editor instances. Multiple documents
  are open at once, **freely mixing notes and sheets**. Each tab owns its own document, **dirty state**,
  and **undo/redo stack** (delegated to that editor). Puffinote owns **document lifecycle** for N live
  editors: create on open, dispose on close, prompt-to-save on close-with-unsaved. With more tabs open
  than fit, the strip **scrolls the active tab into view** whenever it changes — by the shortest scroll
  that does it, so a tab already on screen never moves. Otherwise opening a document from the sidebar can
  leave the one mark of *which* document you are in off the end of the strip.
- **Saving flashes "— Saved" in the title bar** for a few seconds, whichever editor is behind the tab, and
  whether the save came from ⌘S or from auto-save. ⌘S is the one command in the shell whose entire point is
  that nothing happens: no dialog, no visible change, and the only acknowledgement was the tab's dirty dot
  going away — which you had to be watching the tab strip to see. Cellestra's standalone window settled
  this for itself; a notebook tab wants the same answer, and it belongs to the shell rather than to any
  one editor because ⌘S in the shell means the same thing behind all of them. The dot and the badge never
  appear together: an edit after the save brings the dot back and takes the badge with it, because by then
  the save it refers to is out of date.
- **Zoom is remembered per tab**, in the notebook's session alongside the tab list, and restored with the
  tab. The alternative — one zoom for the window — has to be wrong for somebody in a notebook of mixed
  types, because the size that suits a page of prose is rarely the size that suits a grid of numbers.
  A document nobody has zoomed carries no entry at all and opens at whatever its **editor's own** config
  says, which is how a sheet already inherits Cellestra's remembered zoom; this layer records only the
  reader's per-document departures from that. Closing a tab forgets its zoom, which is what "restored with
  the tab" means — the durable answer for a whole type is that editor's own setting, in Preferences.
- **Embedding contract:** Puffinote hosts the **existing** embeddable controls — `Typage.Control`'s
  `MarkdownEditor`/`MarkdownEditorView` for `.md`, and `Cellestra.Editor`'s `SpreadsheetEditorView` for
  `.ods` — **unchanged**. A small shell-side **pane abstraction** (`IEditorPane`: *view to mount*,
  *title*, *is-dirty*, *load/save/save-as*, *the command catalog for this editor*, *the outline*, *the
  status line*) is what Puffinote talks to, so a future file type is "implement one pane + drop in a
  control." (Named `IEditorPane` to avoid colliding with Cellestra's **existing** `IEditorHost`, which is
  the *opposite* direction — the editor delegating file ops outward to its embedder; a `SheetPane`
  implements that `IEditorHost`.)
  - This abstraction is designed against the two real editors, not guessed at — and one is already
    embed-ready: `Cellestra.Editor.IEditorHost`'s own doc-comment anticipates "a notes-app shell driving
    the editor from a file tree." (Mirrors Proposal 0001's stance on deferring the cross-app embed
    protocol until there's a real consumer — now there is.)
  - **Asymmetry resolved (Phase 1):** Cellestra already exposed its file-host + `IAppEnvironment` seams;
    Typage did not — its file ops lived in `Typage.Desktop`. A symmetric file-host seam,
    **`MarkdownDocumentSession`**, was extracted into `Typage.Control` (in the *editor*, never a reach-in
    from Puffinote), keeping the layering clean. See ARCHITECTURE §4.
- **Save/dirty/close semantics** are the sub-apps' own (atomic write, encoding/line-ending preservation
  for Typage; `.ods` correctness + unmodeled-content preservation for Cellestra). Puffinote never
  reimplements save.
- **The active tab drives context:** the active editor supplies the merged palette commands (§7.4), the
  outline panel (§7.2.4), the status bar, and which shortcut set is layered (§7.6).

### 7.4 Command Menu (merged)

- Puffinote has its **own** shell command catalog (open notebook, toggle sidebar, focus tree, new file,
  next/previous tab, open shortcuts editor, switch panel, …), built on the shared `AppCommand` and run
  through the shared `PaletteListView` (⌘K, fuzzy-ranked).
- **Merge rule:** when an editor tab is active, the palette shows **Puffinote's shell commands merged
  with the active editor's catalog** (Typage's or Cellestra's). When no document is open, the palette
  shows only Puffinote's commands. Each catalog is still **defined once, in its own app** — Puffinote
  *composes* the lists at display time; it does not fork or duplicate them (preserves Cellestra Golden
  Rule #4 and Typage's single-catalog rule).
- Command identity is namespaced (shell vs editor) so the shortcuts editor and conflict-detection (§7.6)
  can reason about where a command and its binding come from.

### 7.5 Configuration & settings inheritance

- **Precedence (lowest → highest): sub-app defaults → the sub-app's own config file → Puffinote
  overrides.** When editing inside Puffinote, an editor reads its **own** configuration as the base
  (so a note behaves the way it does in standalone Typage, a sheet the way it does in standalone
  Cellestra), and Puffinote layers its **overrides** on top (e.g. a notebook-wide theme/accent, a
  shared tab width). This matches the literal requirement to "inherit the settings from the
  sub-application's separate configuration files."
- **Storage:** each sub-app keeps its own settings *type* and *file path* (Proposal 0001 Phase 5
  deliberately rejected a shared settings *base type* because they diverge and Typage's is UI-coupled).
  Puffinote adds its **own** config (global/user-level) for shell preferences + a per-notebook override file
  in `.puffinote/`. All three load through the shared tolerant `SettingsStore` (best-effort, never
  throws on a malformed file).
- **Theme:** Puffinote sets the family theme/accent as a top-level override so notes and sheets render as
  one design language via the shared `Tokens` contract; domain-surface colors (Markdown syntax, grid
  cells) remain the editors' own.
- **Editing them:** every app in the family — the shell and all four editors — has a **Preferences dialog
  on ⌘,**, built from the shared `SettingsEditorView` (`Puffinote.Components`): a declared list of typed
  fields over that app's own settings object, which writes nothing until the dialog is accepted. A config
  file nobody can edit without a text editor is a config file that doesn't exist. What the dialogs cover
  is *preferences*, not session state — a recent-files list and a remembered window size are records of
  what you did, and they stay out.
- **Puffinote's is tabbed**, over `TabbedSettingsView`: **General** (Puffinote's own config — the override
  layer), then **Notes**, **Spreadsheets**, **Plain Text** and **Tasks**, one per embedded editor, each
  writing *that editor's own config file* — the same file it reads when it runs standalone. Tabs are named
  for **what they change, not for the program that changes it**: someone looking for how their notes indent
  has no reason to know a note editor is called Typage.
  - **Puffinote reads and writes those files through per-editor mirrors** (`EditorConfig` and friends),
    never by referencing an editor's desktop shell (§9, one-way layering). A mirror models the subset of a
    file Puffinote can act on, defaulted to that editor's own defaults, and **writes back by merging**: keys
    it doesn't model — Typage's theme and recent files, Cellestra's radial bindings and window size, a key
    someone added by hand — are left exactly as they were.
  - **Every tab offers to open its config file** in the OS's default editor for the type, creating it when
    the owning app has never written one. A form only ever exposes the settings someone thought to expose;
    the file has all of them, and that is the escape hatch for the rest.
  - A file is rewritten **only when its values actually changed**, so visiting the dialog is never a reason
    for an app you have never opened to acquire a config file.
- **Applied live:** changing anything in Preferences — Puffinote's overrides *or* an editor's own settings —
  re-resolves the lot and re-seeds the panes that are **already open** (`TabHost.Settings` →
  `IConfigurablePane.ApplyConfig`), not only the next document opened. A setting that doesn't reach the
  document in front of you reads as a setting that didn't work. Every pane type inherits its editor's
  settings this way, so a note, a sheet, a text file and a board each open here the way they open standalone.

### 7.6 Keyboard shortcuts — shared editor, layered resolution, conflict handling

**Shared shortcuts editor.** Puffinote ships a **shared keyboard-shortcuts editor** (built on the shared
`Shortcut`/`ShortcutFormatter`/`AppCommand`) that lists every binding — Puffinote shell + the active/each
editor — and lets the user rebind. The same editor UI and the same bindings are used by **each
separately-exported app**: Typage and Cellestra each expose it over *their* catalog; Puffinote exposes it
over the **merged** set. A binding customized in a sub-app is honored when that editor runs inside
Puffinote (per the config-inheritance rule, §7.5).

**Runtime resolution — context-dependent by focus.** Puffinote defines a **moderate reserved set** of
global shortcuts that the shell always wins: **Command Menu (⌘K), quick-open (⌘O), toggle/focus
sidebar, focus editor, next/previous/close tab, new file, and open notebook.** (This is the "minimal" shell
essentials plus tab navigation, new-file, and quick-open — chosen for predictable chrome while leaving
editors most of the keyspace.) Resolution:

1. A key in the **reserved global set** is handled by Puffinote **regardless of focus** — an editor can
   never shadow it.
2. **Any other** shortcut is dispatched to **whoever has focus**: if the editor is focused, the editor's
   binding wins; otherwise the shell/sidebar binding applies.

The reserved set is **documented** and surfaced in the shortcuts editor (reserved keys are marked and not
reassignable to editor commands from within Puffinote).

**Conflict handling.** Puffinote's own shortcuts are chosen to **avoid overlapping** the editors' common
bindings. If, despite that, a **sub-app's configured shortcut collides with a Puffinote reserved key**,
then on launch Puffinote raises a **friendly warning modal** (via the shared `MessageDialog`/overlay
host) that:

- names the specific conflicting shortcut and the two commands involved,
- explains which one Puffinote will honor (the reserved global one) and therefore what will be shadowed,
- offers a **one-click path to the shortcuts editor** to rebind, and a "keep as-is" dismissal.

This is a launch-time, non-blocking, actionable notice — never a silent shadow (Nielsen #5/#9).

### 7.7 CLI (`puffinote`) — unified router

- Puffinote ships a **`puffinote` CLI**, a fourth consumer of the same engines (no Avalonia, no network),
  built on the shared `Puffinote.Components.Core` `CliHost`/`CliException` harness — the same harness the
  `typage` and `cellestra` CLIs use.
- **Shape — unified router:** `puffinote` adds **notebook-level** commands (e.g. list the tree, full-text
  **search** the notebook, resolve/open a path, read notebook/config info) **and routes** to the sub-app command
  lists so agents have one entry point:
  - `puffinote md <typage-subcommand> …` → the `typage` command set (tokens, html, outline, stats, cat…).
  - `puffinote sheet <cellestra-subcommand> …` → the `cellestra` command set (cat, get, eval, sheets,
    info, functions, validate, recalc, diff…).
  - `puffinote <notebook-command> …` → tree/search/info scoped to the notebook.
- Routing **reuses** each sub-CLI's command definitions (they remain the single source of truth); the
  standalone `typage` and `cellestra` binaries keep working unchanged (§7.8). Conventions carry over:
  reads to stdout; edits are atomic and never overwrite without `--in-place`/`-o`; user errors print a
  clean `puffinote: …` line with a non-zero exit; a stack trace means a real bug.

### 7.8 Sub-apps remain independent (hard requirement)

Each sub-application must **remain independently compilable and runnable**, and must keep:

- its **own build/run** (`dotnet run --project Typage.Desktop`, `dotnet run --project Cellestra.Desktop`),
- its **own persisted configuration** (own type + path; §7.5),
- its **own Command Menu catalog** (defined once in the app),
- its **own CLI command list** (`typage`, `cellestra`) — which the `puffinote` CLI *routes to*, not
  *replaces*,
- its **own shortcuts** working when exported separately (§7.6).

Puffinote is strictly **additive**: a consumer of the editors and the shared library. Removing Puffinote
from the repo must leave both sub-apps fully functional. (Enforced by the one-way layering, §9, and by
keeping both sub-app solutions building green independently — Proposal 0001's green-to-green discipline.)

---

## 8. Precise behavior & defaults (settled — no guessing)

These pin the "everyone expects it but nobody wrote it down" behaviors, in the style of Cellestra §10 /
Typage §12. Where a behavior belongs to an editor, Puffinote **delegates** and does not reinterpret it.

- **Notebook open:** opening a folder that is not a notebook yet simply treats it as one (creates `.puffinote/`
  lazily on first state write). No import step.
- **Opening a file already open** focuses its existing tab rather than opening a duplicate.
- **Dirty & close:** closing a dirty tab prompts save/discard/cancel; closing the notebook/window prompts
  once per dirty tab (or a single consolidated prompt). Undo/redo, revert, and external-reload are the
  **editor's** behavior, unchanged.
- **Save routing** is the editor's (atomic temp-then-rename; encoding/line-endings preserved for `.md`;
  `.ods` `mimetype`-first + OpenFormula + unmodeled-content-preserved for `.ods`). Puffinote never writes
  document bytes itself.
- **Non-supported file activation:** preview if a previewer exists (images), else offer Open-in-default /
  Reveal-in-Finder. Never opens an empty editor for an unknown type.
- **Shortcut precedence:** reserved global set > focused surface (§7.6). The reserved set is fixed and
  documented; editor commands can't be bound onto a reserved key from within Puffinote.
- **Config precedence:** sub-app defaults < sub-app config file < Puffinote overrides (§7.5).
- **Theme default:** follows OS light/dark; a notebook may pin a theme; accent comes from Puffinote's token
  override.
- **Theme, where it is chosen:** **Preferences → General → Appearance → "Theme"** — *Follow System*
  (the default), *Light*, or *Dark* — and **only** there. It is asked once, for the shell, and applies to
  every embedded editor with it: a window showing a note beside a sheet cannot be half light, so a theme
  row on the per-editor tabs would be offering a choice the shell could not honour. The editors' own theme
  settings stay in their own files, untouched by a save from here, and are what they wear when each runs
  standalone (where each offers the same three options in its own Preferences). Applied on the spot, and
  layered across the two scopes like every other setting here, so a notebook's committed
  `.puffinote/settings.json` can pin an appearance for everyone who opens it.
- **Tabs:** new documents open **after** the active tab; `⌘{`/`⌘}` (or platform equivalents) switch tabs;
  a middle-click / `⌘W` closes (with the dirty prompt). Tab order is persisted per notebook.
- **Close All Tabs (`⌥⌘W`):** closes every open tab, prompting per dirty tab exactly as `⌘W` does;
  cancelling one prompt stops the run and leaves the remaining tabs open. It is **not** in the reserved
  set (§7.6) — `⌘W` already guarantees a tab can always be closed — so it stays rebindable.
- **Close Other Tabs:** closes every tab **except one** — the tab that was right-clicked when run from the
  tab strip's context menu, the active tab when run from the palette or the File menu. Same per-tab
  prompting and stop-on-Cancel as Close All. The kept tab is **activated first**, so it's the document in
  front whether or not the run completes. No default gesture (there's no platform convention for one); it's
  rebindable like any non-reserved command, and disabled when fewer than two tabs are open.
- **Tab context menu:** right-clicking a tab offers the three closing verbs — Close Tab, Close Other Tabs,
  Close All Tabs — all scoped to the tab *clicked*, not the active one. Closing is all it offers: the
  file-level commands (rename, reveal, delete) belong to the tree row that owns the file (§7.2.2), not to
  a view of it.
- **Optional tab bar:** the strip can be hidden, for a cleaner reading surface. Two controls, deliberately
  distinct: a **Preferences** toggle ("Show the tab bar", §7.5) is the *durable* choice, and a **Toggle Tab
  Bar** command in the palette/View menu flips it for **this session only**, writing nothing — so hiding
  the strip to read one long note doesn't silently become your default. Assigning the resolved settings
  (opening a notebook, accepting Preferences) re-asserts the persisted choice over a session toggle.
  - Unlike the tab-width override (§7.5) this is a **plain preference, not an override**: the strip is the
    shell's own chrome and no embedded editor has an opinion about it, so "unset" means Puffinote's
    default (shown) rather than "inherit". It is still nullable so the two scopes layer — a notebook that
    hides the strip wins over a user who shows it.
  - Hidden, it needs **no handle** to get back from (contrast the collapsed sidebar, §7.2.1): the tabs stay
    open, `⌘⇧[`/`⌘⇧]` still walk them, `⌘O` still reaches any file, and the title bar still names the
    active document — so nothing becomes unreachable. The command has **no default gesture** (no platform
    convention) and is non-reserved, hence rebindable. It stays **enabled with no notebook open**, where it
    sets what happens when you next open a file.
  - An **empty** editor area shows no strip either way — "show the tab bar" is not "show an empty bar".
- **Optional tab bar:** the strip can be hidden, for people who navigate by ⌘O and ⌘⇧[ / ⌘⇧] and want the
  chrome back. Two surfaces, deliberately different in reach:
  - **Preferences → Appearance → "Show the tab bar"** is the durable choice. It is a plain shell
    preference with a default of *shown*, **not** an override in the §7.5 sense — the strip is Puffinote's
    own chrome and no editor has a setting underneath it to inherit — but it still layers across the two
    scopes, so a notebook's committed `.puffinote/settings.json` can hide it for everyone who opens that
    notebook.
  - **Toggle Tab Bar** (palette + View menu) flips it for **this session only** and writes nothing, so
    hiding the strip to read one long note never becomes your default. Re-resolving the preference —
    opening a notebook, or accepting Preferences — re-asserts it over any session toggle. No default
    gesture (no platform convention); non-reserved, so it's rebindable. Enabled even with nothing open,
    since it sets what happens when you next open a file.
  - Hidden or shown, the strip only ever draws when at least one tab is open — "show the tab bar" is not
    "show an empty bar" — and hiding it closes nothing: the tabs stay open, ⌘⇧[ / ⌘⇧] still walk them, and
    the title bar still names the active document. That is why it needs no counterpart to the collapsed
    sidebar's handle (§7.2.1): nothing becomes unreachable.

*(Anything not pinned here defers to the owning editor's PRD: Typage §7.4/§8/§12 for Markdown behavior,
Cellestra §8/§10 for spreadsheet behavior. If a shell behavior is genuinely unspecified, stop and ask,
then record it here — the doc is the source of truth.)*

---

## 9. Architecture & layering

**One-way dependency direction (non-negotiable):**

```
   puffinote CLI ─┐                         Puffinote.Desktop (shell)
                  │                          │   embeds, never forks
                  ▼                          ▼
   Puffinote.Components.Core        Typage.Control   Cellestra grid/editor
   (CLI harness, no Avalonia)          │                 │
                  ▲                     ▼                 ▼
                  └───────  Puffinote.Components  ◄────────┘   (shared shell/design system)
                                    │
                        Typage.Engine   Cellestra.Core        (pure engines — zero UI, zero Puffinote)
```

- **`Puffinote.Desktop`** (new, macOS-first shell, assembly `Puffinote`): the window, native menu bar,
  file tree + sidebar panels, tab host, merged palette, shortcuts editor, notebook/config services. It
  **references** `Typage.Control`, **`Cellestra.Editor`** (the embeddable editor library — not
  `Cellestra.Desktop`), and `Puffinote.Components`. It contains **no** Markdown or spreadsheet logic.
- **`Puffinote.Cli`** (new): the unified CLI, referencing `Puffinote.Components.Core` + the two sub-CLIs'
  command definitions. No Avalonia.
- **Embedding seams** live in the editors / shared library, not as reach-ins from Puffinote (§7.3).
- **Nothing references Puffinote.** The engines and the shared library stay app-agnostic (Proposal 0001
  §4.4). Removing Puffinote leaves both sub-apps intact (§7.8).
- **Tech stack** matches the family: C# on **.NET 10**, **Avalonia 12.0.4**, **xUnit v3** headless UI
  tests, MIT. Versions pinned centrally in the repo-root `Directory.Packages.props`; nullable-as-error;
  the dependency-allowlist CI gate extends to the new projects.
- **Repository home:** the Puffinote projects live under a new **top-level `app/`** directory
  (`app/Puffinote.Desktop`, `app/Puffinote.Cli`) with their own `.slnx`, signaling Puffinote as the
  composite *product* distinct from the two `features/` editors. Projects under `app/` inherit the
  repo-root build conventions automatically, exactly as `shared/` and `features/` do.

*(A full ARCHITECTURE.md — folder structure, the `IEditorHost` embedding contract, the tab/document
lifecycle, the search index — is a companion doc written when implementation starts, mirroring the
sub-apps' `docs/ARCHITECTURE.md`.)*

---

## 10. Testing & definition of done

- **Definition of done for any task:** `dotnet build` clean (zero new warnings — `WarningsAsErrors=
  nullable` is solution-wide), `dotnet test` green, and **both sub-app solutions still build & test green
  independently** (§7.8).
- **Headless-first:** shell logic (notebook model, tab lifecycle, merged-catalog composition, shortcut
  resolution + conflict detection, config precedence, CLI routing) lives in pure/testable helpers with
  xUnit v3 tests — not baked into views — following Cellestra's "put the logic in Core, keep the UI a
  thin call" rule and its headless-UI gotchas (null-drawing env var, no-GPU constraints).
- **CLI tests** exercise `puffinote` routing end-to-end in-process (as `Typage.Cli.Tests` /
  `Cellestra.Cli.Tests` do), asserting that notebook commands and both routed sub-command sets behave.
- **Conflict-warning** has an explicit test: a seeded sub-app binding on a reserved key produces the
  documented modal and the reserved command still wins at runtime.

---

## 11. Roadmap (phased, green-to-green)

Each phase leaves all three apps building clean with green tests (Proposal 0001's discipline). Stop after
any phase that delivers value.

- **Phase 0 — Shell scaffold. ✅ Landed.** `app/Puffinote.Desktop` (assembly `Puffinote`) window + menu
  (macOS native bar / in-window bar elsewhere) + `Puffinote.Components` theming (puffin-orange accent);
  a `NotebookService`/`NotebookNode` opens a notebook and a collapsible sidebar renders a **read-only, lazy file
  tree**; a placeholder editor area. `Puffinote.Tests` (pure notebook-model) + `Puffinote.UiTests` (headless
  smoke) green; both sub-app solutions still build independently. No editors embedded yet.
- **Phase 1 — Single-editor host. ✅ Landed.** Extracted the Typage file-host seam
  (`MarkdownDocumentSession`) into `Typage.Control`; added the shell-side `IEditorPane` + `MarkdownPane` +
  `EditorPaneFactory`; `MainWindow` mounts a single pane, opens a `.md` from the tree (double-click/Enter),
  and **Save** (⌘S) writes byte-faithfully. Round-trip + pane tests green; both sub-apps still build. Zero
  editor forks. (Prompt-on-dirty-replace + multiple tabs come with Phase 2.)
- **Phase 2 — Both editors + tabs. ✅ Landed.** Added `SheetPane` (embeds Cellestra's
  `SpreadsheetEditorView`, which self-hosts its `IAppEnvironment`; the pane implements `IEditorHost` +
  stream load / atomic snapshot save) and a `TabHost` owning the N-document lifecycle — open/focus/close,
  a tab strip, next/prev (⌘⇧[ / ⌘⇧]) and close (⌘W) — mixing notes and sheets, each tab with its own
  dirty state. Dirty-close (and window-close) prompts run through the shared `OverlayHost` +
  `ChoiceDialog` (Save/Don't-Save/Cancel), resolving the Phase 1 deferral. 11 headless + 9 pure tests
  green; both sub-apps still build.
- **Phase 3 — Merged palette + shortcuts. ✅ Landed.** A single shell `AppCommand` catalog drives the
  menu, key bindings, and a **Command Menu (⌘K)** that merges Puffinote's commands with the active
  editor's catalog (`IEditorPane.Commands` — Cellestra's full self-hosted catalog; a minimal Typage set
  until its catalog is extracted). The **moderate reserved set** (⌘K, ⌘⇧O, ⌘\, ⌘W, ⌘⇧[ / ⌘⇧]) wins over
  the focused editor via a tunnelling handler; everything else is focus-dispatched. A **conflict scanner**
  warns once (shared `MessageDialog`) when an editor's shortcut collides with a reserved key. 13 pure + 13
  headless green; both sub-apps still build. (Full Typage catalog extraction + the shortcuts *editor* UI
  are Phase 5.)
- **Phase 4 — Sidebar completion.** Full keyboard-operable tree, collapsible sidebar, the **search /
  outline / recent** panels, and the **quick-open (⌘O)** switcher.
- **Phase 5 — Config inheritance + shortcuts editor.** Sub-app-base → Puffinote-override precedence; the
  shared shortcuts editor over the merged set, working standalone in each sub-app too.
- **Phase 6 — `puffinote` CLI.** Notebook commands + routing to the two sub-CLIs on the shared `CliHost`.

---

## 12. Risks & mitigations

| Risk | Mitigation |
|---|---|
| **Cellestra is macOS-only → blocks cross-platform Puffinote** | 1.0 is macOS-first by decision (§5). Cross-platform is gated on Cellestra going cross-platform; called out, not hidden. |
| **Embedding tempts editor forks / reach-ins** | The `IEditorHost` seam + Proposal 0001's "no upward reference, seam in the editor/library" rule. Reviewed API, not special-cases. |
| **Two editors, two undo/config models, one shell** | Delegate everything domain-owned; Puffinote owns only lifecycle + composition. Tests assert delegation. |
| **Shortcut collisions across three catalogs** | Reserved-global-set + focus dispatch (§7.6), a documented reserved list, and the launch conflict modal. |
| **Shell becomes a dumping ground / duplicates the library** | Shared primitives stay in `Puffinote.Components`; Puffinote only *composes*. Bar to add to the library unchanged (rule-of-three, proven duplication). |
| **Search over `.ods`** | Reuse `Cellestra.Core`/CLI to read cell contents; background index; degrade gracefully if a workbook can't be indexed. |
| **Breaking a standalone app** | Green-to-green across all three every phase; one-way layering; both sub-app solutions build independently in CI. |

---

## 13. Open questions / decisions to resolve before scaffolding

Resolved in this draft (from review):

1. ~~Workspace model~~ → **single notebook folder** (§7.1).
2. ~~Config precedence~~ → **sub-app base, Puffinote overrides** (§7.5).
3. ~~Shortcut runtime resolution~~ → **reserved global set + focus dispatch** (§7.6).
4. ~~Platform scope~~ → **macOS-first for 1.0** (§5).
5. ~~Editor panes~~ → **tabs** (not splits) for 1.0 (§7.3).
6. ~~CLI shape~~ → **unified router over the sub-CLIs** (§7.7).
7. ~~Non-supported files~~ → **show all; preview/open-externally** (§7.2).
8. ~~Sidebar panels~~ → **tree + search + outline + recent** (§7.2).
9. ~~Project home~~ → **top-level `app/` directory** with its own `.slnx` (§9).
10. ~~Quick-open~~ → **yes, a dedicated ⌘O fuzzy quick-open** (§7.2.5).
11. ~~`.puffinote/` & version control~~ → **split gitignore: ignore volatile state, allow durable
    settings** (§7.1).
12. ~~Reserved global shortcut set~~ → **moderate** — palette, quick-open, sidebar toggle/focus, focus
    editor, tab next/prev/close, new file, open notebook (§7.6).

13. ~~Cellestra embeddable assembly~~ → **`Cellestra.Editor`** (`SpreadsheetEditorView`); it already
    exposes the `IEditorHost` + `IAppEnvironment` seams (§9, ARCHITECTURE §4).

14. ~~Typage file-host seam~~ → **extracted as `MarkdownDocumentSession` in `Typage.Control`** (Phase 1;
    ARCHITECTURE §4).

Still open:

- **Search index format** inside `.puffinote/` (kept out of git per §7.1; on-disk shape is an
  ARCHITECTURE detail).
- **Default keys** for the reserved commands (the *set* is fixed in §7.6; the exact chords are pinned in
  ARCHITECTURE alongside each editor's existing bindings to guarantee no overlap).

*(Per house rule: when a shell behavior is genuinely unspecified, stop and ask, then record the
resolution here — the doc is the source of truth.)*
