# Puffinote — Architecture & Design

> **Status:** Living document. It explains *how* Puffinote is put together and *why*. For *what* and
> *why-at-the-product-level*, see [`PRD.md`](PRD.md). This mirrors the sibling architecture docs
> ([Cellestra](../../features/EditSpreadsheets/docs/ARCHITECTURE.md),
> [Typage](../../features/EditMarkdown/docs/ARCHITECTURE.md)) — same repo, same tooling, same
> golden-rule discipline. Where this file and the PRD disagree on a *design* detail, this file wins; the
> PRD wins on *product* scope.

---

## 1. The big picture in one breath

Puffinote is **a shell that composes two existing editors** — it adds no document engine of its own. The
hard, bug-prone document logic already lives (and is tested) in `Typage.Engine` and `Cellestra.Core`;
the two editing *surfaces* already exist as embeddable Avalonia control libraries (`Typage.Control`,
`Cellestra.Editor`); and the shared shell/design-system already exists as `Puffinote.Components`
(Proposal 0001). Puffinote is the **fourth consumer** of that stack: a window with a notebook-rooted file
tree, tabs, a merged Command Menu, a shortcuts editor, and a routing CLI.

```
   ┌──────────────────────────────────────────────────────────────┐
   │                        Puffinote.Desktop                       │
   │  Notebook file tree · Sidebar panels · Tab host · Merged palette  │
   │  Shortcuts editor · Native macOS menu · Notebook/config services  │
   └───────────────┬───────────────────────────────┬───────────────┘
        hosts, per tab (composition — never fork)   │ notebook-level ops
                   ▼                                 ▼
   ┌───────────────────────────┐        ┌──────────────────────────┐
   │  Typage.Control            │        │  Cellestra.Editor         │
   │  MarkdownEditor(View)      │        │  SpreadsheetEditorView    │
   │  IEmbedResolver            │        │  IEditorHost · IAppEnv    │
   └─────────────┬─────────────┘        └────────────┬─────────────┘
                 ▼                                    ▼
   ┌───────────────────────────┐        ┌──────────────────────────┐
   │  Typage.Engine (pure)      │        │  Cellestra.Core (pure)    │
   └───────────────────────────┘        └──────────────────────────┘
                 ▲                                    ▲
                 └────────  Puffinote.Components  ─────┘   (shared shell/design system)
                                    ▲
                        Puffinote.Components.Core (non-UI: CLI harness)
```

The whole point of the layering: **removing Puffinote leaves both sub-apps fully working** (PRD §7.8).
Puffinote references down into the editors and the shared library; nothing references *up* into Puffinote.

---

## 2. Tech stack & version pins

Puffinote inherits the family stack verbatim — it introduces **no new dependency line**.

| Thing | Pinned value |
|---|---|
| Runtime / TFM | C# on **.NET 10** (`net10.0`) |
| UI | **Avalonia 12.0.4** (compiled bindings on; SkiaSharp 3 transitive) |
| Tests | **xUnit v3**; headless UI via `Avalonia.Headless.XUnit` 12.0.4 |
| CLI arg parsing (`Puffinote.Cli`) | `System.CommandLine` (as the sub-CLIs) |
| License | MIT |

Versions are pinned centrally in the repo-root `Directory.Packages.props`; projects under `app/` inherit
`Directory.Build.props` (`net10.0`, nullable, `WarningsAsErrors=nullable`) automatically. **No new NuGet
package** above the .NET BCL + Avalonia + OS-services base without a stated reason; **no npm, ever**; icons
are the shared local Lucide SVGs. The dependency-allowlist CI gate (carried from the sub-apps) extends to
the two new projects.

---

## 3. The projects

Two new projects, both thin:

- **`Puffinote.Desktop`** (Avalonia executable, assembly `Puffinote`, **macOS-first**): the window, native
  menu bar, sidebar (file tree + panels), tab host, merged palette, shortcuts editor, and the
  notebook/config/session services. **References** `Typage.Control`, `Cellestra.Editor`,
  `Puffinote.Components` (and, transitively, the two engines). It contains **no** Markdown or spreadsheet
  logic — every document operation is delegated to the embedded editor.
- **`Puffinote.Cli`** (console executable `puffinote`, **no Avalonia**): the unified router. References
  `Puffinote.Components.Core` (the shared `CliHost`/`CliException` harness) plus the two sub-CLIs' command
  definitions, and the two engines for notebook-level reads. Routes `md …`→Typage and `sheet …`→Cellestra,
  and adds notebook commands (tree, search, info).

No new class-library is expected: shared primitives already live in `Puffinote.Components`; anything
genuinely reusable that emerges goes *there*, not into a Puffinote-local library (keeps the floor shared).

---

## 4. The embedding model (the one real design task)

Puffinote must host a *heterogeneous* set of editors behind a *uniform* tab. Two facts from the codebase
shape this:

1. **Cellestra is already embed-ready.** `Cellestra.Editor` exposes `SpreadsheetEditorView` and an
   **`IEditorHost`** interface whose doc-comment literally anticipates "a notes-app shell driving the
   editor from a file tree." `IEditorHost` is the **editor → embedder** direction: file ops the editor
   *delegates outward* (`NewWorkbookAsync`, `OpenWorkbookAsync`, `SaveWorkbookAsync`, `SaveWorkbookAsAsync`,
   CSV import/export). Cellestra's command catalog reaches the shell through `IAppEnvironment` (Proposal
   0001 §5 — the per-environment `AppCommands`).
2. **Typage — file-host seam extracted (Phase 1).** `Typage.Control` exposes `MarkdownEditor`/
   `MarkdownEditorView` and an `IEmbedResolver` (for in-document embeds), but file operations lived in
   `Typage.Desktop`'s `MainWindow`. Phase 1 added **`MarkdownDocumentSession`** to `Typage.Control` — a
   reusable file host (path + encoding via the engine's pure `TextFileCodec`, byte-faithful atomic
   temp-then-rename save) so `Typage.Control` can be driven by any shell. It lives in the *editor*, not as
   a reach-in from Puffinote (golden-rule layering). (Converging `Typage.Desktop` onto the same session —
   it still has its own working copy — is optional future cleanup, not required, since it already works.)

### 4.1 The host-side abstraction: `IEditorPane` (new, shell-side)

Cellestra's `IEditorHost` is the *editor asking the host* to do file ops. Puffinote also needs the
opposite: a **host-side** handle over "whatever editor a tab contains," so the tab host, palette, outline,
and status bar can treat a note and a sheet uniformly. To avoid colliding with the existing
`IEditorHost`, the shell-side abstraction is named **`IEditorPane`** (in `Puffinote.Desktop`):

```
IEditorPane                         // one per open tab; wraps a concrete editor view
  Control    View        { get; }   // the MarkdownEditorView or SpreadsheetEditorView to mount
  string     Title       { get; }   // file name; drives the tab label
  bool       IsDirty     { get; }   // delegated to the editor's own dirty state
  event      DirtyChanged;          // repaint the tab dot
  Task       LoadAsync(path);       // open a file into this pane
  Task       SaveAsync(); SaveAsAsync();
  Task<bool> PromptCloseAsync();    // returns false to cancel a close (unsaved prompt)
  IReadOnlyList<AppCommand> Commands { get; }   // this editor's catalog (for the merged palette)
  IOutline?  Outline     { get; }   // headings / sheets+charts (for the outline panel)
  IStatusLine Status     { get; }   // the editor's own status content
  void        Dispose();            // tab close → dispose the editor + engine snapshot
```

- **Two implementations**, each a thin adapter over the real control: `MarkdownPane` (wraps
  `Typage.Control`, *implements the new Typage file-host seam*) and `SheetPane` (wraps `Cellestra.Editor`,
  *implements Cellestra's `IEditorHost` and supplies an `IAppEnvironment`*). Neither reaches into editor
  internals; each only wires the published seams.
- **A future file type** = one new `IEditorPane` implementation + its control. No shell rewrite. This is
  why the abstraction is designed *against the two real editors that exist now*, not guessed at (Proposal
  0001 deferred the cross-app embed protocol until there was a real consumer — Puffinote is it).
- **`IEditorPaneFactory`** maps a file to a pane: `.md`→`MarkdownPane`, `.ods`→`SheetPane`, and anything
  else that reads as text→`PlainTextPane` (Plaintain). The specific editors are asked first — Markdown
  and a spreadsheet are both "text" to a decoder — and the text route is a **fallback, not a whitelist**:
  a known-extension list routes fast, and anything else opens if a probe over its first 8 KB finds no
  bytes a text file wouldn't have. An extension that *declares* a binary format (`.png`, `.zip`, …) is
  believed over its bytes, and everything left gets the open-externally action (PRD §7.3).

### 4.2 What Puffinote deliberately does *not* own

Save mechanics (atomic temp-then-rename; encoding/line-endings for `.md`; `.ods` `mimetype`-first +
OpenFormula + unmodeled-content-preserved), undo/redo, and editing behavior stay entirely inside the
editors. `IEditorPane.SaveAsync()` calls the editor's save; Puffinote never writes document bytes.

---

## 5. The notebook & file tree

- A **notebook is a single root folder** (PRD §7.1). `NotebookService` opens one at a time, watches it with a
  `FileSystemWatcher` (add/remove/rename → tree refresh; changes to *open* files ride each editor's
  existing external-change flow), and owns switching (prompt-to-save all dirty panes, then reload).
- **Per-notebook state** lives in a `.puffinote/` directory inside the notebook (self-describing, portable):
  session (open tabs, active tab, sidebar collapsed/width), durable per-notebook settings (theme/overrides),
  and the search index. A shipped `.puffinote/.gitignore` **ignores the volatile** (index, session) and
  **allows the durable** (settings) to be committed (PRD §7.1).
- **The file tree** (`Controls/FileTree`) is a custom, virtualized, **fully keyboard-operable** tree
  (PRD §7.2.2): Up/Down/Left/Right/Home/End, type-to-search, Enter-to-open, `F2` rename, new/delete, with
  an **automation peer** and a visible focus ring. It shows **all** files; extension decides the action.

---

## 6. Tabs & document lifecycle

- The main area is a **tab strip** (`Controls/TabStrip`) over a stack of mounted `IEditorPane.View`s.
  `TabHost` owns the **N-document lifecycle**: create a pane via the factory on open, mount/activate,
  dispose on close.
- **Each tab is independent:** its own dirty state and undo/redo (delegated to the editor), its own
  status line and outline. Opening an already-open file focuses its tab rather than duplicating (PRD §8).
- **Close semantics:** closing a dirty tab calls `PromptCloseAsync()` (shared `ConfirmDialog`); closing
  the notebook/window prompts per dirty tab. New tabs open after the active one; `⌘{`/`⌘}` switch; `⌘W`
  closes. Tab order persists per notebook.
- **Close-all** (`shell.closeAllTabs`, `⌥⌘W`) is `TabHost.CloseAllAsync()` — the same per-tab
  `CloseAsync` path the window-close handler uses, so it runs the same dirty prompt and stops on a Cancel.
- **Close-others** (`shell.closeOtherTabs`, no default key) is `TabHost.CloseOthersAsync(keep)`, over that
  same `CloseAsync` path; it activates `keep` first so the survivor is in front even if a Cancel cuts the
  run short. Each tab in the strip carries its **own** `ContextMenu`, built in `TabHost.BuildTabMenu` and
  closed over that tab, so the menu's verbs target the tab that was right-clicked (PRD §8). The shell
  command is the same call keeping `TabHost.ActiveTab`, for the palette and the File menu.
- **The active tab drives context:** it supplies the merged-palette editor commands (§7), the outline
  panel, the status bar, and which shortcut layer is active (§8).

---

## 7. The merged command catalog & palette

- Puffinote has its **own** shell `AppCommand` catalog (`Commands/PuffinoteCommands`): open notebook, toggle
  /focus sidebar, focus editor, new file, next/prev/close tab, quick-open, switch panel, open shortcuts
  editor, …. Built on the shared `AppCommand` and run through the shared `PaletteListView`.
- **Merge, don't fork.** When a tab is active, the palette shows Puffinote's shell catalog **merged with
  the active pane's `Commands`** (Typage's `AppCommands` or Cellestra's env-bound `AppCommands`). Each
  catalog is still defined once in its own app (Cellestra Golden Rule #4; Typage single-catalog rule);
  Puffinote *composes* at display time. Command identity is **namespaced** (`shell:` vs `editor:`) so the
  shortcuts editor and conflict detection can attribute a binding.
- **Quick-open (⌘O)** is a *separate* surface from the palette (PRD §7.2.5): same `FuzzyMatcher` /
  `PaletteListView`, but ranks *notebook files* by name + recency, not commands.

---

## 8. Shortcuts: reserved set, focus dispatch, conflict detection

- **One binding model**, the shared `Shortcut` / `ShortcutFormatter` / `AppCommand`. The **shared
  shortcuts editor** (a `Puffinote.Components` control, so each standalone app can host it too) lists
  every binding and rebinds it; a binding customized in a sub-app is honored inside Puffinote (config
  inheritance, §9).
- **Reserved global set (moderate, fixed, documented):** palette (⌘K), quick-open (⌘O), toggle/focus
  sidebar, focus editor, tab next/prev/close, new file, open notebook. `ShortcutRouter` resolves a keypress:
  1. a key in the **reserved set** → handled by the shell **regardless of focus** (an editor can't shadow
     it);
  2. **any other** key → dispatched to **whoever has focus** (the focused pane's binding wins; otherwise
     the shell/sidebar binding). The pane reports focus via a `IsTextInputFocused`-style flag (Cellestra
     already exposes one on `IAppEnvironment`).
- **Conflict detection (`ShortcutConflictScanner`)** runs at launch: if a **sub-app's configured binding
  collides with a reserved key**, raise the friendly `MessageDialog` (PRD §7.6) that names the two
  commands, says the reserved one wins, and links to the shortcuts editor. Non-blocking, actionable, never
  a silent shadow. Reserved keys are marked non-reassignable-to-editor-commands in the editor UI.

---

## 9. Configuration inheritance

- **Precedence (low → high): sub-app defaults → the sub-app's own config file → Puffinote overrides**
  (PRD §7.5). A `MarkdownPane`/`SheetPane` constructs its editor with the editor's **own** settings as the
  base (so a note behaves as in standalone Typage, a sheet as in standalone Cellestra), then Puffinote
  applies its overrides (notebook theme/accent, shared tab width) on top.
- **Storage:** each sub-app keeps its own settings *type* + path (Proposal 0001 Phase 5 rejected a shared
  settings *base type* — they diverge and Typage's is UI-coupled). Puffinote adds its own user-level config
  + a per-notebook override in `.puffinote/`. All load through the shared tolerant `SettingsStore`.
- **Theme** is a top-level Puffinote override via the shared `Tokens` contract, so notes and sheets read
  as one design language; domain-surface colors (Markdown syntax via `MarkdownTheme`, grid cells via
  `GridPalette`) stay the editors' own.

---

## 10. Sidebar panels & search

Panels are switchable and each keyboard-focusable (PRD §7.2): **file tree**, **notebook search**, **outline**,
**recent**.

- **Notebook search** (`Search/NotebookIndex`): a background full-text index over the notebook. Notes index raw
  Markdown; sheets index cell **contents** via `Cellestra.Core` (the same read path the CLI uses). Indexing
  status is shown, never blocks; the index is stored in `.puffinote/` (git-ignored). Results open the hit
  in a tab.
- **Outline** reads the **active pane's** `IOutline`: Typage's heading outline (already computed — see
  `typage outline`), or Cellestra's sheet+chart list. Selecting an entry navigates within the editor.
- **Recent** reuses the shared `RecentFiles`, scoped to the notebook.

---

## 11. The `puffinote` CLI (router)

- Built on `Puffinote.Components.Core`'s `CliHost`/`CliException` (the same harness as `typage`/
  `cellestra`), so conventions match: reads → stdout; edits atomic, never overwrite without `--in-place`/
  `-o`; clean `puffinote: …` error line + non-zero exit; a stack trace means a real bug.
- **Router shape:** `puffinote md <typage-subcommand>` and `puffinote sheet <cellestra-subcommand>` **reuse
  the sub-CLIs' own command definitions** (single source of truth — the standalone `typage`/`cellestra`
  binaries keep working unchanged), and `puffinote <notebook-command>` adds tree/search/info scoped to the
  notebook. The router composes the three command trees under one `System.CommandLine` root.

---

## 12. Threading

The UI thread owns the tree, tabs, and editor mounts. Each editor keeps its own threading contract
(Typage's background tokenizer over immutable snapshots; Cellestra's off-thread recalc/I-O). The notebook
**search index** builds off the UI thread and marshals results back. Puffinote adds no shared mutable
state across editors — panes are isolated.

---

## 13. Folder structure

Puffinote lives under a **new top-level `app/`** directory (PRD §9), signaling the composite *product*
distinct from the two `features/` editors. It inherits the repo-root tooling automatically, exactly as
`shared/` and `features/` do.

```
app/                                     # the composite product
├── Puffinote.slnx                       # the solution (references the two features + shared)
├── README.md   AGENTS.md
│
├── Puffinote.Desktop/                   # the Avalonia 12 app — assembly "Puffinote", macOS-first
│   ├── Puffinote.Desktop.csproj         # refs Typage.Control, Cellestra.Editor, Puffinote.Components
│   ├── Program.cs  App.axaml (+ .cs)
│   ├── Info.plist                       # bundle id; .md + .ods associations
│   ├── Menu/ (NativeMenu.axaml)         # macOS menu bar → merged catalog
│   ├── Commands/ (PuffinoteCommands.cs, MergedCatalog.cs)
│   ├── Shell/
│   │   ├── NotebookService.cs  NotebookState.cs        # single-notebook model + .puffinote/ state
│   │   ├── PuffinoteSettings.cs                  # via shared SettingsStore
│   │   └── ShortcutRouter.cs  ShortcutConflictScanner.cs
│   ├── Panes/
│   │   ├── IEditorPane.cs  IEditorPaneFactory.cs  IOutline.cs
│   │   ├── MarkdownPane.cs               # wraps Typage.Control (+ new Typage file-host seam)
│   │   ├── SheetPane.cs                  # wraps Cellestra.Editor (impl IEditorHost + IAppEnvironment)
│   │   ├── EditorConfig.cs               # + NoteConfig / SheetConfig / PlainTextConfig / BoardConfig:
│   │   │                                 #   mirrors of each editor's OWN settings file (§7.5), read and
│   │   │                                 #   merge-written so unmodelled keys survive; never a ref into
│   │   │                                 #   an editor's desktop shell
│   │   └── PreviewPane.cs                # images / open-externally
│   ├── Controls/
│   │   ├── FileTree/ (FileTree, FileTreeNode, FileTreeAutomationPeer)
│   │   ├── TabStrip/ (TabStrip, TabHost)
│   │   ├── Sidebar/ (Sidebar, PanelSwitcher)
│   │   └── QuickOpen/ (QuickOpenOverlay)
│   ├── Panels/ (SearchPanel, OutlinePanel, RecentPanel)
│   ├── Search/ (NotebookIndex, IndexWorker)
│   ├── Views/ (MainWindow, StatusBar, ShortcutsEditorView)
│   └── Services/ (DialogService via Puffinote.Components OverlayHost, RecentNotebooks)
│
├── Puffinote.Cli/                       # the `puffinote` router — no Avalonia
│   ├── Puffinote.Cli.csproj             # refs Puffinote.Components.Core + both sub-CLIs + engines
│   ├── Program.cs  CliApplication.cs     # composes md/sheet/notebook command trees
│   └── Commands/ (NotebookCommands.cs, RouteMarkdown.cs, RouteSheet.cs)
│
├── Puffinote.Tests/                     # shell logic, xUnit v3 (pure/headless helpers)
│   └── (NotebookServiceTests, TabHostTests, MergedCatalogTests,
│        ShortcutRouterTests, ConflictScannerTests, ConfigPrecedenceTests)
│
├── Puffinote.UiTests/                   # headless Avalonia, xUnit v3
│   └── (FileTreeKeyboardTests, TabHostMountTests, TestAppBuilder.cs)
│
├── Puffinote.Cli.Tests/                 # end-to-end CLI, in-process (routing + notebook cmds)
│
└── docs/ (PRD.md, ARCHITECTURE.md)      # ← this file
```

*(Landed in `features/EditMarkdown/`: the extracted Typage file-host seam `MarkdownDocumentSession` in
`Typage.Control` (§4). Cellestra needs no structural change — `Cellestra.Editor` already exposes the
seams.)*

---

## 14. Testing strategy

Following the family rule — **put the logic in a pure/testable helper, keep the UI a thin call** — so the
no-GPU headless environment can run it (see the sub-apps' headless gotchas: the null-drawing env var,
`AssertRenders()`, no two `dotnet test`/`build` at once).

- **`Puffinote.Tests`** (fast, no window): the notebook model, tab lifecycle, **merged-catalog composition**,
  **shortcut routing + conflict detection**, **config precedence**, and the search index — the shell's
  real logic lives here.
- **`Puffinote.UiTests`** (headless Avalonia, xUnit v3): the **keyboard-operable file tree**, tab mounting,
  and that menu/palette/keys resolve to the same merged commands.
- **`Puffinote.Cli.Tests`** (in-process): `puffinote` notebook commands **and** both routed sub-command sets
  behave; conventions (stdout, atomic edits) hold.
- **Definition of done:** `dotnet build` clean (zero new warnings), `dotnet test` green, **and both sub-app
  solutions still build & test green independently** (PRD §7.8) — green-to-green across all three.

---

## 15. Key decisions & tradeoffs

| Decision | Why | Tradeoff |
|---|---|---|
| Compose two editors; no new engine | Reuse tested logic; keep layering | Puffinote is at the mercy of two editors' seams |
| Host-side `IEditorPane` (new) distinct from Cellestra's `IEditorHost` | Uniform tab over heterogeneous editors, no name collision | One more small abstraction |
| Extract a Typage file-host seam | Symmetry with Cellestra; embeddability | A change in `Typage.Control`, coordinated |
| Tabs, not splits, for 1.0 | Faithful to single-doc editors; smaller lift | No side-by-side note+sheet in 1.0 |
| Merge catalogs at display; don't fork | Preserves each app's single-catalog rule | Namespacing + merge logic in the shell |
| Reserved-global-set + focus dispatch | Predictable chrome without stealing the keyspace | A fixed reserved list to maintain |
| Single notebook; state in `.puffinote/` | Portable, self-describing; matches PKM norms | One notebook at a time (multi-root deferred) |
| macOS-first | Cellestra is macOS-only today | Cross-platform gated on Cellestra |
| Top-level `app/`, not `features/` | Signals the composite product | A new top-level convention |

---

## 16. Things that will genuinely evolve

- The **Typage file-host seam** (once extracted) may grow to match Cellestra's `IEditorHost` surface.
- **New `IEditorPane` types** as future interfaces land (canvas, kanban, PDF) — the whole reason for the
  abstraction.
- **Splits/panes** and **multi-root notebooks** (both PRD non-goals for 1.0) will pressure `TabHost` and
  `NotebookService` respectively — designed to extend, not block.
- The **search index** format inside `.puffinote/` as notebooks grow.
- Whether any shell primitive proves reusable enough to graduate into `Puffinote.Components`.
