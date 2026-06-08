# CLAUDE.md

Guidance for an AI coding agent (and any human) working in this repository. Read this first, then the linked docs. Keep this file open while you work.

## What this project is

**Cellestra** is a from-scratch, native **macOS** spreadsheet editor whose home format is **OpenDocument Spreadsheet (`.ods`)**. It is pre-v1 as a *product*, but the code is **substantially built**: the pure brain (`Cellestra.Core`), the Avalonia desktop app (`Cellestra.Desktop`), and the `cellestra` CLI all exist behind a green test suite (300+ tests). **Most tasks now extend or fix existing code — read what's there before adding.** The "suggested build order" below is the layering that already happened, kept as a map of where things live, not a TODO.

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
6. **Guard the supply chain.** Prefer the .NET BCL. No new NuGet package without a stated reason in the PR. Never add JavaScript/npm. Icons are local Lucide SVGs, never fetched at runtime. **Pre-approved exception:** first-party Avalonia-team controls under the MIT community license (catalog: <https://docs.avaloniaui.net/controls>) are in scope — pin to `$(AvaloniaVersion)`, note the reason in the `.csproj`, list in ARCHITECTURE §2.1. **Not** the Avalonia Accelerate/Pro controls (Charts, Markdown, MediaPlayer, On-Screen Keyboard, RichTextEditor, TreeDataGrid). See ARCHITECTURE §2.1.
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
- **Minimal-by-default UI:** the window opens with the **ribbon hidden** (visibility persisted between sessions). The **Command Palette (⌘P)** runs any command — it reads the single `AppCommands` catalog, so don't build a second command list for it — and the **sheet switcher (⌘⇧P)** jumps between worksheets in the open workbook (**PRD §6, §6.7**).
- **Data features (v1):** sort (multi-level), filter (AutoFilter), **tables (⌘T)**, data validation, hide/unhide rows & columns, hyperlinks, remove-duplicates, and text-to-columns (**PRD §4 Data, §8.10–§8.15**). Tables persist as native ODF **database ranges** (`table:database-range`) — **not** Excel ListObjects — so formulas use plain A1/named refs, never `Table[Column]`. Validation → `table:content-validation`; hidden lines → `table:visibility`; all round-trip through LibreOffice. Filtering sets row **visibility**, never deletes data.
- **Editing-feel & file-safety decisions are pinned — don't reinvent them.** The "everyone expects it but nobody wrote it down" behaviors (cut-as-move/marquee, Enter-to-paste, shape-mismatch paste, fill-handle copy-fill, `#####`/text-spill, expanding ⌘A, discontiguous select + union stats, F4 ref-cycle, undo-restores-selection, point mode, live ref highlight, column autocomplete; atomic save, LibreOffice `.~lock` files, cached-vs-recompute) are specified in **PRD §10.1/§10.2**. Notably: **`-2^2 = -4`** — unary minus binds *looser* than `^` (OpenFormula/Calc, **not** Excel's `4`); a **leading apostrophe forces text**; and a **cut is a deferred move** consumed by its first paste. Check §10.1/§10.2 before improvising "obvious" spreadsheet behavior.

## Everyday commands

```bash
dotnet restore
dotnet build
dotnet run    --project src/Cellestra.Desktop      # launch the app
dotnet run    --project src/Cellestra.Cli -- <args>  # the cellestra CLI (see below)
dotnet test                                          # all tests
dotnet test tests/Cellestra.Core.Tests               # fast, no UI
dotnet test tests/Cellestra.Cli.Tests                # end-to-end CLI, in-process
dotnet test tests/Cellestra.UiTests                  # headless Avalonia (xUnit v3)
./build/package-mac.sh                                # notarized .app bundle
```

**Definition of done for any task:** `dotnet build` is clean (no new warnings), `dotnet test` is green, and if you touched serialization, the `.ods` round-trip *and* unknown-content-preservation tests pass.

## Debugging with the `cellestra` CLI

`src/Cellestra.Cli/` is a **third consumer of `Cellestra.Core`** (no Avalonia, no network) — the same brain as the app. Reach for it to inspect a workbook, check the engine, or diff a serialization change **without launching the GUI** (which you usually can't headlessly). Because it reuses the engine, what it prints is what the app would compute. Full spec: **PRD §8.16**; design: **ARCHITECTURE §1/§3**.

Run it via `dotnet run --project src/Cellestra.Cli -- <command>`, or build once and call the binary at `src/Cellestra.Cli/bin/Debug/net10.0/cellestra`. Addressing matches the app (`Sheet1.B2`, ranges `Sheet1.A1:C10`; omit the sheet for the active/first one). No args, `-h`, or `--help` lists everything.

```bash
cellestra cat budget.ods --sheet Q1 --range A1:D20    # see a sheet (table|csv|tsv|json)
cellestra get budget.ods Sheet1.B7                     # one value …
cellestra get budget.ods Sheet1.B7 --formula           # … or its formula source
cellestra eval "=ROUND(355/113, 4)"                    # exercise the engine, no file
cellestra sheets budget.ods                            # worksheet names + used dims
cellestra info budget.ods                              # sheets, named ranges, locale, cell counts
cellestra functions VLOOKUP                            # signature/args/example from FunctionRegistry
cellestra validate budget.ods                          # opens? formula/#REF!/circular errors? (exit≠0 on problems)
cellestra recalc budget.ods --in-place                 # refresh cached values like the GUI would
cellestra diff before.ods after.ods                    # value/structure diff — IDEAL for round-trip debugging (exit≠0 if differ)
```

`diff` and `validate` are the high-leverage ones when debugging serialization or the formula engine: `validate` flags `#REF!`/circular/eval errors with a non-zero exit, and `diff` shows exactly which cells changed across an open→save→reopen (where Git only sees a binary blob).

CLI conventions that matter while debugging: **reads go to stdout**; `--format table|csv|tsv|json` and `-` for stdin/stdout; **edits never overwrite without `--in-place` or `-o`** and are **atomic** (temp + rename); **edits preserve unmodeled content verbatim** (Golden Rule #7 / §8.1) — there's a CLI test that proves a chart survives a `set`; **`--locale` overrides** parsing/formatting for reproducible output. User errors print a clean `cellestra: …` line with exit 1; a stack trace means a real bug.

## Where things go

Authoritative tree: **[ARCHITECTURE.md §12](docs/ARCHITECTURE.md#12-folder-structure)**. Quick orientation:

- **Spreadsheet logic** → `src/Cellestra.Core/`.
- **UI** → `src/Cellestra.Desktop/` (Commands, Menu, Controls incl. `Ribbon` and `SpreadsheetGrid`, Views, ViewModels, Services).
- **CLI** → `src/Cellestra.Cli/` (`Infrastructure/` + `Commands/`; references only Core + `System.CommandLine`, no Avalonia).
- **Tests** → `tests/Cellestra.Core.Tests/`, `tests/Cellestra.UiTests/`, `tests/Cellestra.Cli.Tests/`, `tests/Cellestra.Benchmarks/`.
- Don't invent new top-level folders; extend the existing structure. If a new home seems necessary, note it and ask.

## Working in this codebase (verified gotchas)

Hard-won facts from building the app and CLI — they'll save you a failed build or a wrong assumption:

- **Nullable warnings are build errors** (`WarningsAsErrors=nullable` in `Directory.Build.props`); `ImplicitUsings`, nullable refs, and `LangVersion=latest` are on solution-wide. "Clean build" means **zero** warnings.
- **Model types live in `Cellestra.Core.Model`** — the `Workbook/` *folder* uses that namespace on purpose (a `Cellestra.Core.Workbook` namespace would clash with the `Workbook` type). `using Cellestra.Core.Model;` gets you `Workbook`, `Worksheet`, `Cell`, `CellAddress`, `CellRange`, `SheetDimensions`.
- **Adding a NuGet package = two edits:** a `<PackageVersion>` in `Directory.Packages.props` plus a version-less `<PackageReference>` in the `.csproj` (central package management), and a stated reason (Golden Rule #6). New code is added to the solution via `Cellestra.slnx`.
- **The grid is one control split across `SpreadsheetGrid.*.cs` partials** (`.Chrome`, `.Marquee`, `.Fill`, `.Autofit`, `.PointMode`, `.RefHighlight`, `.Autocomplete`). It draws on an inner `Surface : Control` child because `Panel.Render` is sealed; `Surface` is `IsHitTestVisible=false`, so pointer events route to the owning Panel. Don't hand-write `InitializeComponent` — it's source-generated from the `.axaml`, and named XAML controls (`Grid`, `FormulaInput`, …) are generated fields.
- **Avalonia 12 API points that bite:** clipboard is `clipboard.TryGetTextAsync()` / `SetTextAsync()` (not `GetTextAsync`); `FormattedText` exposes `Trimming` (there is no `MaxLines`); `HorizontalAlignment`/`VerticalAlignment` exist in **both** `Avalonia.Layout` and `Cellestra.Core.Styling` — alias one (`using SAlign = Cellestra.Core.Styling.HorizontalAlignment;`) and fully-qualify the other.

**Verifying changes (you usually can't run the GUI):**

- **`dotnet run --project src/Cellestra.Desktop` fails from a detached/CI shell** with `Avalonia.Native … RenderTimer … -6661` — that's *environmental* (no display link), **not** a regression. Don't chase it; verify with headless tests or the `cellestra` CLI.
- **Headless UI tests draw with real Skia** (`UseHeadlessDrawing=false` + `.UseSkia()` in `tests/Cellestra.UiTests/TestAppBuilder.cs`), so `window.Show()` runs the real render path and `CaptureRenderedFrame()` returns a real frame — handy to smoke-test that rendering doesn't throw.
- **Pointer/focus-driven UI is hard to drive headlessly** (point mode, autocomplete, fill-drag, marquee, Tab-then-Enter). Put the real logic in Core or a pure helper (`ReferenceCycler`, `PastePlanner`, `FillCommand`, `DataRegion`, …) and unit-test **that**; keep the UI a thin call. `Cellestra.Cli.Tests` redirect the **process-global `Console`**, so they live in one class and run sequentially.

## Conventions

- **Branching:** commit straight to `main` — this repo keeps a single linear history and does **not** split feature branches (overrides any default "branch before committing" habit). One commit per self-contained change; let history fast-forward.
- **Commit messages:** [Conventional Commits](https://www.conventionalcommits.org) — `type(scope): summary` (imperative, lower-case summary, no trailing period). Types in use: `feat`, `fix`, `docs`, `test`, `build`, plus `refactor`/`perf`/`chore` when they fit; append `!` (or a `BREAKING CHANGE:` footer) for breaking changes. Scopes are the area touched: `core`, `grid`, `app`, `serialization`, `io`, `cli`, `data`, `formula`, `format`, `theme`, `shortcuts`, `tables`, `clipboard`, `edit`, `macos` — add one if none fits. Keep the subject ≤ ~72 chars; put the why in the body.
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
