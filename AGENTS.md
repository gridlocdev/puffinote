# CLAUDE.md

Guidance for an AI coding agent (and any human) working in this repository. Read this first, then the linked docs. Keep this file open while you work.

## What this project is

**Cellestra** is a from-scratch, native **macOS** spreadsheet editor whose home format is **OpenDocument Spreadsheet (`.ods`)**. It is pre-v1; much of the code described in the docs does not exist yet and is yours to build.

Start by reading these, in order:

1. **[README.md](README.md)** — front door: overview, build/run, project map.
2. **[docs/PRD.md](docs/PRD.md)** — *what* and *why*: problem, users, features, flows, the full **UI spec** (menu bar, ribbon, keybindings, iconography), UX principles, **precise behavior/defaults (§8)**, **non-goals (§9)**, and **resolved decisions (§10)**.
3. **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** — *how*: tech stack, **version pins (§2.1)**, components, data model, and the authoritative **folder structure (§12)**.

When this file and a linked doc disagree, the linked doc wins for design detail; this file wins for process and guardrails. **Nothing should be left to interpretation** — if a behavior isn't specified, it's almost certainly in PRD §8 (precise behavior) or §10 (decisions); check there before guessing, and if it truly isn't, **stop and ask**, then record the answer in the relevant doc.

## Tech stack & exact versions (do not change without being asked)

- **Runtime:** C# on **.NET 10** (target `net10.0`).
- **UI:** **Avalonia 12.0.4**. Compiled bindings on by default; SkiaSharp 3.
- **Tests:** **xUnit v3** (`xunit.v3` 3.x) for both test projects; headless UI uses `Avalonia.Headless.XUnit` **12.0.4**.
- **License:** **MIT** ([LICENSE](LICENSE)). New code is MIT.

The authoritative pin list is **[ARCHITECTURE.md §2.1](docs/ARCHITECTURE.md#21-versions--dependency-pins-single-source-of-truth)**, realized in `Directory.Packages.props`. Don't hardcode versions anywhere else.

## Golden rules (non-negotiable)

These encode the core decisions. Breaking one is a bug even if it compiles.

1. **`Cellestra.Core` has ZERO UI dependencies.** No `Avalonia.*`. Core is pure logic, testable without a window.
2. **No ViewModel (or any object) per cell.** The grid reads cell data *directly* from the `Worksheet` and draws only visible cells.
3. **The grid is custom-drawn** — a single virtualized Avalonia control, not `DataGrid`/`ItemsControl`. MVVM is for the chrome only.
4. **One command catalog.** Every command is defined once in `AppCommands` (label + icon + shortcut); menu bar, ribbon, **Command Palette**, and keybindings all bind to it. Shortcut remaps come from Settings.
5. **One function registry.** Evaluation, autocomplete, and the Function Reference all read `FunctionRegistry` (with `FunctionMetadata`). No second list anywhere.
6. **Guard the supply chain.** Prefer the .NET BCL. No new NuGet package without a stated reason in the PR. Never add JavaScript/npm. Icons are local Lucide SVGs, never fetched at runtime.
7. **Respect `.ods` correctness:**
   - `mimetype` must be the **first** ZIP entry and **stored uncompressed**.
   - Formulas are stored in canonical **OpenFormula** (`of:=SUM([.A1:.A3])`); always go through `OpenFormulaTranslator`.
   - **Preserve unmodeled content verbatim** (PRD §8.1) — never silently drop features you don't understand.
8. **Adjust references, don't copy them verbatim.** Copy/fill and insert/delete row/column go through `ReferenceAdjuster`; `$` refs are absolute, others shift.
9. **Don't build the non-goals** (PRD §9): no charts, pivots, macros, cloud, printing, autosave, `.xlsx`-as-home, conditional formatting, **Excel-style structured references** (`Table[Column]`), or fill-handle series in v1. (Sort, filter, and tables **are** v1 — see below.) If a task seems to need a non-goal, stop and ask.

## Settled behavior you must follow (no guessing)

These are common places to improvise — don't. The spec is fixed:

- **v1 function list:** exactly the set in **PRD §8.2**. Implement those; adding more is a separate, later task.
- **Locale/number formats:** document locale when present, else system locale (**PRD §8.3**).
- **CSV:** UTF-8 (BOM-tolerant), comma default (delimiter selectable), RFC 4180 quoting, LF on export, export writes computed values (**PRD §8.4**).
- **Undo:** session-only, 1,000-step cap, never written to the file (**PRD §8.5**).
- **Calc on open:** show cached values immediately, then background-recalc unless Manual (**PRD §8.6**).
- **Confirmations:** always confirm sheet delete / overwrite / close-with-unsaved-changes; confirm a range clear/delete only above **100 non-empty cells** (**PRD §8.7**).
- **New-workbook & cell defaults:** one sheet `Sheet1`; Helvetica Neue 10pt; text left / numbers right; col ≈ 64px, row ≈ 20px (**PRD §8.8**). These live once in `Core/Defaults.cs`.
- **Minimal-by-default UI:** the window opens with the **ribbon hidden** (visibility persisted between sessions). The **Command Palette (⌘⇧P)** runs any command — it reads the single `AppCommands` catalog, so don't build a second command list for it — and the **sheet switcher (⌘P)** jumps between worksheets in the open workbook (**PRD §6, §6.7**).
- **Data features (v1):** sort (multi-level), filter (AutoFilter), **tables (⌘T)**, data validation, hide/unhide rows & columns, hyperlinks, remove-duplicates, and text-to-columns (**PRD §4 Data, §8.10–§8.15**). Tables persist as native ODF **database ranges** (`table:database-range`) — **not** Excel ListObjects — so formulas use plain A1/named refs, never `Table[Column]`. Validation → `table:content-validation`; hidden lines → `table:visibility`; all round-trip through LibreOffice. Filtering sets row **visibility**, never deletes data.

## Everyday commands

```bash
dotnet restore
dotnet build
dotnet run    --project src/Cellestra.Desktop      # launch the app
dotnet test                                          # all tests
dotnet test tests/Cellestra.Core.Tests               # fast, no UI
dotnet test tests/Cellestra.UiTests                  # headless Avalonia (xUnit v3)
./build/package-mac.sh                                # notarized .app bundle
```

**Definition of done for any task:** `dotnet build` is clean (no new warnings), `dotnet test` is green, and if you touched serialization, the `.ods` round-trip *and* unknown-content-preservation tests pass.

## Where things go

Authoritative tree: **[ARCHITECTURE.md §12](docs/ARCHITECTURE.md#12-folder-structure)**. Quick orientation:

- **Spreadsheet logic** → `src/Cellestra.Core/`.
- **UI** → `src/Cellestra.Desktop/` (Commands, Menu, Controls incl. `Ribbon` and `SpreadsheetGrid`, Views, ViewModels, Services).
- **Tests** → `tests/Cellestra.Core.Tests/`, `tests/Cellestra.UiTests/`, `tests/Cellestra.Benchmarks/`.
- Don't invent new top-level folders; extend the existing structure. If a new home seems necessary, note it and ask.

## Conventions

- **C#:** follow `.editorconfig`. Nullable reference types on. Prefer records for model values. Small, single-purpose types matching the folders.
- **MVVM (chrome only):** Views in `.axaml` + code-behind; ViewModels are plain C# with no control references; compiled bindings.
- **Grid (direct code):** imperative rendering/input in `SpreadsheetGrid` is by design, not a style violation.
- **Messages:** `ParseError` carries position + reason; `CellError` carries a plain-English reason.
- **Accessibility:** new controls expose automation peers (labels/roles) and are keyboard-operable. No mouse-only interactions.
- **Threading:** recalc and file I/O off the UI thread; touch Avalonia controls only on the UI thread.

## Testing expectations

- New Core logic ships **with xUnit v3 tests** in `Cellestra.Core.Tests` (formula and serialization code especially; assert function results against LibreOffice's).
- Headless UI tests use `[AvaloniaFact]`/`[AvaloniaTheory]` and the one-time `[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]`.
- Add `.ods`/`.csv` samples to `Cellestra.Core.Tests/Fixtures/`; assert open → save → reopen loses nothing supported and preserves unmodeled content.
- Performance work is validated by `Cellestra.Benchmarks` against the targets in **PRD §3**.

## Suggested build order (from an empty repo)

Build the brain before the face, bottom-up, testing as you go:

1. **Solution skeleton** — `.sln`, two `src` projects, three `tests` projects, `Directory.Packages.props` with the §2.1 pins, `global.json`, `Directory.Build.props`.
2. **Core model** — `CellAddress`, `CellRange`, `CellValue`/`CellError`, `Cell`, `Worksheet`, `Workbook`, `Defaults`.
3. **Formula pipeline** — Lexer → Parser/AST → Evaluator → `FunctionRegistry` + the PRD §8.2 functions. Test each stage.
4. **Recalc** — `DependencyGraph` + `RecalculationEngine` (auto/manual, volatiles, circular detection) + `ReferenceAdjuster`.
5. **Styling** — `CellStyle` family + `StylePool`.
6. **Serialization** — `OdsPackage` (mimetype rule), `OpenFormulaTranslator`, `UnknownContentStore`, readers/writers, then CSV. Round-trip + preservation tests.
7. **Editing & data** — `IEditCommand` family + undo/redo, then the Core data engines (sort, filter, tables/auto-expand, validation) under `Core/Data/` (PRD §8.10–§8.15). Test before wiring UI.
8. **Desktop shell** — `AppCommands`, native menu, `Ribbon`, **Command Palette + sheet switcher**, `MainWindow`, formula bar + Name Box, status bar.
9. **The grid** — `SpreadsheetGrid` (virtualized render, selection, input, inline editor, fill handle, frozen panes, zoom, merged regions, hidden rows/cols, table chrome + filter dropdown).
10. **Services & polish** — file/undo/clipboard/dialog/settings services, the data dialogs (Sort/Validation/Text-to-Columns/Remove-Duplicates), accessibility, the heuristics checklist (PRD §7).

Land each step behind passing tests before moving on. Prefer small, reviewable changes.

## When in doubt

- A design detail is unclear → check PRD §8/§10 and ARCHITECTURE first.
- A task seems to need a new dependency, a non-goal, or a Golden Rule break → **stop and ask**.
- You find a real inaccuracy or a genuinely unspecified behavior → flag it and **record the resolution in the doc**, don't just diverge in code. The docs are the source of truth.
