# Proposal 0002 — God-file refactor: taming the oversized files

- **Status:** Tier 1 (the big four) **done**; Tier 2 `app` `MainWindow` **done**; rest of Tier 2 + Tier 3 planned. See §6.
- **Author:** (you) · drafted with Claude
- **Affects:** `features/EditSpreadsheets` (Cellestra), `features/EditMarkdown` (Typage), `app/` (Puffinote shell)

---

## 1. Summary

15 non-test files exceed 400 lines; the top six exceed 1,000, and two exceed 2,000. This proposal
breaks them up **without changing behavior**, using the partial-class convention this codebase already
relies on (`MarkdownEditor` → 10 partials, `SpreadsheetGrid` → 18 partials). No `#region`s, no new
NuGet, no new top-level projects — just moving cohesive clusters into `Type.Concern.cs` siblings and,
where the logic is genuinely framework-free, extracting real collaborator types/services.

Every step keeps its area's solution green (build clean, tests pass, `dotnet format` no-ops) and lands
as its own `refactor(<scope>): …` commit straight on `main`.

---

## 2. Two refactoring modes

Pick per file — don't mix blindly.

| Mode | When | Files |
|---|---|---|
| **A. Partial split** — same type, sliced into `Type.Concern.cs` | Avalonia-constrained types (a `Window`/`Control`/`UserControl` must stay one class) **and** types with `…ForTest` reach-in hooks that a new type would break | Both `MainWindow`s, `SpreadsheetEditorView`, `SpreadsheetGrid`, `RadialSettingsDialog` |
| **B. True extraction** — move logic into new files/services/types | Pure logic with no framework coupling; **and** shell-orchestration that `app/AGENTS.md` says belongs in `Shell/` | `OdsContentXml`, `CommandBarDrawer`, `ChartControl` helpers, engine files, Puffinote `MainWindow`'s orchestration |

**Mode A is mechanical and low-risk** (move members between partials of one class — no signature,
visibility, or test-surface change). **Mode B carries design decisions** and needs tests. Where a type
has `…ForTest` hooks, prefer A; only extract to a new type when the seam is genuinely independent.

The reference pattern (from `MarkdownEditor`): the **root file** owns the constructor, **all shared
private fields**, and nested types; each partial is a stateless slice over those fields, opening with
its own scoped `using`s. Same namespace, same folder, `Type.Concern.cs` naming.

---

## 3. Non-negotiable guardrails

- **Behavior-preserving.** Pure reorganization — no logic changes ride along. Spot a bug mid-move? Note
  it, don't fix it in the same commit.
- **Never touch generated code** — `InitializeComponent` comes from `.axaml`; don't hand-author the
  generated half of a XAML partial.
- **No `#region`** (matches convention), **no new NuGet, no new top-level folders/projects.**
  Extractions land in existing folders (`Shell/`, `Document/`, `Serialization/Ods/`, …).
- **Per-area definition of done, before each commit:** `dotnet build <area>.slnx` clean (warnings **are**
  errors), `dotnet test <area>.slnx` green, `dotnet format <area>.slnx --verify-no-changes` exits zero.
- **One file per commit**, `refactor(<scope>): split <File> into concern partials`, co-author line.
  Trunk-based straight onto `main`, no branches.
- **Sequencing:** parallelize *exploration* across areas; do *writes* single-threaded within an area
  (conflicting partials are costly to reconcile). Different areas proceed independently.

---

## 4. Tier 1 — the four biggest (this proposal executes these)

**All four landed** — one `refactor(<scope>): …` commit each, straight on `main`, area solution
green (build clean + tests + `dotnet format`). Final root sizes: 4a 504, 4b 417, 4c 180, 4d 44 lines.

### 4a. `Cellestra.Editor/Views/SpreadsheetEditorView.axaml.cs` — 2,235 → ~6 partials · **Mode A** · ✅ done
Already `partial` for XAML. Root keeps fields/ctor/host wiring; add siblings:
- `.Sheets.cs` — add/chart/rename/delete/reset (~300)
- `.Tabs.cs` — sheet-tab strip build + drag-reorder + inline rename (~320, self-contained)
- `.Clipboard.cs` — copy/cut/paste/paste-special (~270)
- `.FormulaBar.cs` — formula bar + name box (~180)
- `.Dialogs.cs` — sort/dup/validation/find/format launchers (~250)
- `.KeyBindings.cs` — key bindings + gating; **extract pure static gesture helpers**
  (`ShouldYieldToTextInput`, `IsPlainKeyGesture`, `IsTextEditingChord`…) to a testable `KeyGestureRules`.

### 4b. `Cellestra.Editor/Controls/SpreadsheetGrid/SpreadsheetGrid.cs` — 2,196 → 4 more partials · **Mode A** · ✅ done
Root partial of an already-18-way split; carve the core the same way:
- `.Rendering.cs` (~400) · `.Pointer.cs` (~390) · `.Keyboard.cs` (~250) · `.Editor.cs` (inline editor, ~310)
- `.Layers.cs` — move nested `Surface` + `EditorDecorationLayer` out of root.

### 4c. `Typage.Desktop/Views/MainWindow.cs` — 1,802 → ~6 partials · **Mode A** · ✅ done
Convert to `partial`; root keeps fields, ctor, nested `CheckItem`, test seams:
- `.Menu.cs` (~350) · `.Toolbar.cs` (~210) · `.Files.cs` (~320) · `.Watcher.cs` (autosave/recovery/
  external-change/conflict, ~210) · `.Dialogs.cs` · `.Theme.cs` (~80).
- *As landed:* split into `.Menu` / `.Files` / `.Dialogs` / `.Toolbar` / `.Watcher` / `.Theme` **plus a
  small `.Status.cs`** (status-bar build + title/count/caret updates), keeping each file single-purpose.

### 4d. `Cellestra.Core/Serialization/Ods/OdsContentXml.cs` — 1,639 → partial-static + 1 extraction · **Mode B** · ✅ done
`partial static class` along a **feature axis** (read+write of a feature stay together):
- `.Write.cs` · `.Read.cs` · `.Validation.cs` · `.DatabaseRanges.cs`
- **Extract `StyleRegistry`** (self-contained nested class) to its own file.
- Round-trip tests in `Cellestra.Core.Tests/Serialization` are the safety net.

---

## 5. Tiers 2–3 (planned, not in this pass)

### Tier 2 — large, mixed concerns
- **`app/Puffinote.Desktop/Views/MainWindow.cs` — 1,094 · Mode B + A · ✅ done.** Top layering offender.
  Landed as four commits, each area-green (build clean + tests + `dotnet format`):
  - `RecentFilesList` (`Shell/`) — the recent list + its promotion / rename-remap / delete-drop maintenance,
    now returning whether it changed so the shell re-renders only when it did (+ tests).
  - `SidebarLayout` (`Sidebar/`, not `Shell/` — it owns Avalonia controls, so it lives with the sidebar UI)
    — dedupes the collapse/width logic across `ToggleSidebar`/`ApplySidebarState`/`PersistSession`,
    preserving the two distinct collapse paths (+ tests).
  - `ShellCommandCatalog` (`Shell/`) — the **reserved-key sets + effective-gesture rule** (+ tests). The
    17-entry *table* itself stayed in the window: each entry's action closes over live window state, so
    transplanting it would mean plumbing ~20 delegates back in — the pure, testable rules were the real seam.
  - Partial-split the residual UI into `.Menu` / `.Shortcuts` / `.Mutations` / `.Session` (a `.Shortcuts`
    partial beyond the three planned keeps the shortcuts-editor/conflict concern out of `.Menu`). Root
    1,094 → **186** lines.
- **`RadialSettingsDialog.cs` — 1,026 · A + one B.** Extract the command-picker sub-dialog (~230 + its
  `PickerItem`) to a `CommandPickerOverlay`; partials `.Drag.cs`, `.Confirm.cs`; keep `…ForTest` in root.
- **`ChartControl.cs` — 741 · Mode A.** Per-chart-family partials: `.Cartesian` (~250), `.Pie`, `.Scatter`,
  `.Radar`, `.Legend`; shared math/format in root. (Draw methods already `private static`.)
- **`CommandBarDrawer.cs` — 723 · Mode B.** `partial static` by section: `.Size`, `.Colour`, `.Align`,
  `.Format` (owns `FormatOption`); shared plumbing in root.

### Tier 3 — engine + smaller shell (cleanest, lower urgency)
| File | Lines | Action | Mode |
|---|---|---|---|
| `Typage.Engine/Html/HtmlRenderer.cs` | 612 | `partial static`: `.Blocks`, `.Inline`, `.Escaping` | B |
| `Typage.Engine/Markdown/InlineTokenizer.cs` | 566 | `partial`: `.Constructs` (8 `Try*`), `.Scanning` | A/B |
| `Typage.Engine/Document/TextDocument.cs` | 497 | **Weakest** — carve line-index to `.Lines.cs` only if it earns its keep; tight `_pieces`/`_lineStarts` coupling. Consider deferring. | A |
| `app/…/Panes/TabHost.cs` | 384 | Move nested `TabItem` + `CloseDecision` (non-UI) to own files | B |
| `app/…/Controls/FileTreeView.cs` | 512 | Optionally extract pure tree-flatten + `FindMatch` typeahead. Low priority. | B |
| `app/…/Sidebar/FilesPanel.cs` | 439 | Optionally extract ~115-line chip drag-to-reorder into a chip-strip control. Low priority. | B |

---

## 6. Execution order & status

1. ~~**Tier 1**, one file per area, parallel across areas. Start with **4d (`OdsContentXml`)** and **4b
   (`SpreadsheetGrid`)** — lowest risk.~~ **✅ Done** — all four landed as `refactor` commits on `main`
   (4d `ce8d12b`, 4b `6a63cea`, 4c `6dd950e`, 4a `773b619`); each area solution builds clean and its
   tests are green.
2. ~~**Tier 2 `app` `MainWindow`** as a focused effort — biggest *architectural* win (fixes layering-in-UI),
   adds real test coverage; treat as more than a move.~~ **✅ Done** — four `refactor(shell)` commits on
   `main` (`RecentFilesList` `9cb0872`, `SidebarLayout` `ee96423`, `ShellCommandCatalog` `29140bb`, the
   partial-split `d5448a6`); Puffinote solution builds clean and its tests are green.
3. **Tier 2 remainder** *(next)* — `RadialSettingsDialog`, `ChartControl`, `CommandBarDrawer` — then Tier 3
   opportunistically. **Defer `TextDocument`** unless it causes friction.

**Effort:** Tier 1–2 ≈ 10 files, each a 0.5–1.5h mechanical-plus-verify unit (`app` `MainWindow` larger,
~half a day). Tier 3 is optional polish.

---

## 7. Non-goals

- Not changing any observable behavior, public API shape, or test outcomes.
- Not introducing a line-count lint rule (none exists today; adding one is out of scope).
- Not merging/renaming the existing `MarkdownEditor`/`SpreadsheetGrid` partials.
- Not touching CLI, engine algorithms, or serialization *semantics* — only file organization.
