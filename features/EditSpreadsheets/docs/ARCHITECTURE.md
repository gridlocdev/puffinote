# Cellestra — Architecture & Design

> **Status:** Living document. It explains *how* Cellestra is put together and *why*. For *what* and *why-at-the-product-level*, see `PRD.md`.

---

## 1. The big picture in one breath

Cellestra is a **pure brain with two faces**:

1. **A pure brain (`Cellestra.Core`)** — knows everything about spreadsheets (data, formulas, recalculation, reading/writing `.ods`) and **nothing** about windows, buttons, or drawing. No UI dependencies at all.
2. **A graphical face (`Cellestra.Desktop`)** — an Avalonia app that draws the grid, handles clicks and typing, and talks to the brain.
3. **A command-line face (`Cellestra.Cli`)** — the `cellestra` binary for scripts, CI, and build pipelines. No Avalonia, no network. Because it reuses the same brain, `convert` shares the serializers, `eval` shares the formula engine, and `functions` reads the very same `FunctionRegistry` that powers the app's autocomplete — so CLI output and GUI behaviour can't drift apart.

This separation is the most important decision in the project. The hard, bug-prone logic lives where it can be tested instantly without a window, and *two* different faces ride the same brain.

```
   ┌───────────────────────────────┐   ┌──────────────────────────────┐
   │        Cellestra.Desktop       │   │         Cellestra.Cli         │
   │  macOS menu bar · Command bar │   │  convert · cat · get · set ·  │
   │  Formula bar · Grid · Tabs     │   │  recalc · eval · validate ·   │
   │  Views ↔ ViewModels ↔ Services │   │  diff · functions · …         │
   └───────────────┬────────────────┘   └───────────────┬──────────────┘
                   │  calls into                         │  calls into
                   └──────────────────┬──────────────────┘
                                      ▼
            ┌─────────────────────────────────────────┐
            │             Cellestra.Core                │
            │  Workbook model · Formula engine ·        │
            │  Recalc · Styling · .ods + CSV I/O        │
            │       (no Avalonia, no UI at all)         │
            └─────────────────────────────────────────┘
```

## 2. Tech stack, and why

### Language & runtime: C# on .NET 10
Statically typed, familiar, with a huge trustworthy standard library (ZIP and XML for `.ods` are built in). .NET 10 is the current LTS.

### UI: Avalonia 12 (MVVM, but flexibly)
A mature cross-platform .NET UI framework, close to WPF, modern-looking, native on macOS. **Avalonia 12** reworked rendering for speed in dense scenes (Cellestra's grid), turned on compiled bindings by default, and moved to SkiaSharp 3. It's **MIT-licensed** and is essentially **one dependency from one team** — a good fit for the supply-chain goal, versus the JavaScript world this project avoids.

MVVM is used for the *chrome* (menu, command bar, formula bar, dialogs). It is **not forced** — the grid is a hand-built control using direct code (see §6).

### License: MIT
Cellestra ships under the **MIT License** (see `LICENSE`). All new code is MIT.

### 2.1 Versions & dependency pins (single source of truth)
Exact versions live in **`Directory.Packages.props`** (central package management). This list is the known-good starting set; bump deliberately, never incidentally, and update this section when you do.

| Thing | Pinned value |
|---|---|
| Target framework | `net10.0` (.NET SDK 10.0.x) |
| Avalonia (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`) | 12.0.4 |
| `Avalonia.Controls.ColorPicker` (first-party color picker — Format Cells) | 12.0.4 |
| `Avalonia.Headless.XUnit` (UI tests) | 12.0.4 |
| SkiaSharp | transitive via Avalonia 12 (3.x) — do not pin separately unless forced |
| xUnit | `xunit.v3` 3.x + `xunit.runner.visualstudio` |
| BenchmarkDotNet | latest 0.x |
| Microsoft.Extensions.DependencyInjection | latest matching `net10.0` |
| `System.CommandLine` (CLI arg parsing — `Cellestra.Cli` only) | 2.0.8 |

**Dependency policy:** prefer the .NET base class library. Adding any new package requires a stated reason in the PR. No JavaScript/npm tooling, ever. Icons ship as **local SVG assets** (Lucide), never fetched at runtime.

**Pre-approved exception — first-party Avalonia controls.** The optional controls maintained by the **Avalonia team** under the same **MIT community license** (the catalog at <https://docs.avaloniaui.net/controls>) are in scope without a fresh supply-chain review: they're the same team, license, and version line (`12.0.4`) as Avalonia itself, so they don't widen the trust surface. Pin each to `$(AvaloniaVersion)`, add a one-line reason in the `.csproj`, and list it in the table above. In use today: **`Avalonia.Controls.ColorPicker`** (the Format Cells color pickers; its Fluent control theme is merged in `App.axaml` via `avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml`). **Excluded:** anything requiring the **Avalonia Accelerate / Pro** license — at time of writing **Charts, Markdown, MediaPlayer, On-Screen Keyboard, RichTextEditor, and TreeDataGrid** — which are a different license and stay out of scope (and several overlap the §9 non-goals anyway). Everything else (any other third-party NuGet) still needs the stated-reason review above.

### Why not the alternatives (short version)
- **JavaScript/Electron/Tauri:** the dependency/supply-chain surface this project avoids.
- **LibreOffice/OnlyOffice/Numbers:** the products this project reacts against.
- **Rust (egui/Slint):** great, and Core *could* be Rust, but C#+Avalonia gives text editing, IME, and macOS integration for free.
- **raylib:** lovely drawing, but no text editing/clipboard/accessibility — too much to rebuild.

## 3. The projects

- **`Cellestra.Core`** — a plain class library; everything that defines what a spreadsheet *is*. No Avalonia. Most tests point here. References `Cellestra.Charts` (to embed/read chart sheets and resolve their ranges to numbers).
- **`Cellestra.Charts`** — the standalone chart store: the chart-sheet model (`ChartDefinition`/`ChartSeries`/`ChartType`) and a from-scratch ODF `chart:chart` serializer (`Ods/ChartDocument`). No Avalonia, **no third-party charting library** (every chart type is a custom implementation), and deliberately no dependency on Core — chart data ranges are stored as plain ODF address strings, so Core can reference it without a cycle. Chart **drawing** lives in the desktop `ChartControl`.
- **`Cellestra.Desktop`** — the Avalonia executable: window, menu bar, command bar, grid, dialogs, and a thin **Services** layer bridging user actions to Core.
- **`Cellestra.Cli`** — the `cellestra` console executable. References only Core + `System.CommandLine`; **no Avalonia, no network**. A thin `Infrastructure` layer (`Workspace` for load/recalc/atomic-save, `Cells`/`Output`/`Locale` helpers) mirrors what the Desktop `Services` layer does, so edits are atomic and **preserve unmodeled content verbatim** (§8.1) exactly like the app. One static method per command, composed in `CliApplication` so tests can drive the same surface in-process.

## 4. The data model (lives in Core)

- **`Workbook`** — the whole document: worksheets + document-wide **named ranges**, **style pool**, and **document settings** (locale, calculation mode, active sheet).
- **`Worksheet`** — one sheet: cells, row/column sizes and **visibility (hidden rows/columns)**, **merged-cell regions**, frozen-pane state, **tables/database ranges**, the active **filter** and **sort** state, and **data-validation** rules.
- **`Cell`** — a value and/or formula, plus a reference to a style, an optional **hyperlink**, and a **cached computed value** (so a freshly opened file can display before recalculation).
- **`Table`** (a.k.a. database range) — a named, rectangular region with a header row, banded-style reference, and **auto-expand** rules; carries its own `FilterSpec`/`SortSpec`. Maps to ODF `table:database-range`.
- **`FilterSpec`** — per-column criteria (by-value sets or conditions); drives row visibility. **`SortSpec`** — an ordered list of sort levels (column + key + direction).
- **`DataValidation`** — a rule (list / number / date / text-length) bound to a range, with input message and error response (Stop/Warning/Info). Maps to ODF `table:content-validation`.
- **`CellValue`** — typed contents: number, text, boolean, error, or empty.
- **`CellError`** — `#DIV/0!`, `#REF!`, `#VALUE!`, `#NAME?`, `#N/A`, each with a human-readable reason.
- **`CellAddress`** — a location; can be **sheet-qualified** (`Sheet2.A1`) and remembers **absolute vs relative** (`$A$1`).
- **`CellRange`** — a rectangle of addresses.
- **`Defaults`** — a single static home for the new-workbook/cell defaults from `PRD.md` §8.8 (default font, sizes, alignment, sheet naming), so they're defined once.

> **Sizing rule:** there is **no object per cell on the UI side**. A sheet may hold hundreds of thousands of cells; the grid reads from the `Worksheet` directly and draws only visible cells.

## 5. Inside the Core brain

### Formula engine (a pipeline)
**Lexer** → **Parser/AST** → **Evaluator** (with an `EvaluationContext` that resolves cells, ranges, named ranges, other sheets) → **Functions** in a registry.

**`FunctionRegistry` metadata.** Each function entry carries everything three consumers need, so they never disagree: canonical **name**, **category**, one-line **summary**, an **argument list** (each: name, type, required/optional, repeatable), **return type**, and one **example**. The evaluator uses it for arity/type checks; autocomplete uses it for hints; the in-app Function Reference renders it as docs.

### Dependency tracking & recalculation
A **dependency graph** records which cells each formula reads. The **recalculation engine** recomputes in topological order, touching only what's affected, honoring the **calculation mode** (automatic/manual) and a manual "recalculate now." **Volatile functions** (TODAY, NOW, RAND) recompute every pass. **Circular references** are detected and reported.

### Reference adjustment
`ReferenceAdjuster` rewrites references on **copy/fill** (relative refs shift by the offset; absolute `$` refs don't) and on **insert/delete row/column** (formulas updated, dependency graph re-pointed). Fiddly and high-stakes — one place, heavy tests.

### Tables, sorting, filtering & validation
All pure logic in Core, so it's testable without a window.
- **Sorting** — a stable, multi-level sort that reorders rows (cells **and** their styles) and routes formula references through `ReferenceAdjuster`, honoring locale collation.
- **Filtering** — `FilterSpec` evaluation marks rows hidden/visible; it sets row visibility rather than deleting data.
- **Tables** — a `Table` tracks its range and **auto-expand** logic (absorbing edits at the bottom/right edge), keeping banding, filters, and whole-column formulas in step. Tables serialize as ODF database ranges; **no Excel structured-reference syntax** is emitted or parsed (`PRD.md` §9).
- **Data validation** — `DataValidation` rules are checked on edit commit; the rule set is the model behind in-cell dropdowns and the validation dialog.

### Styling
`CellStyle` gathers font, fill, borders, alignment, wrapping, and number format. A **`StylePool`** stores each distinct style once and hands out references, matching how `.ods` uses named styles. Banded table styling is expressed through the same style pool.

### Editing commands & undo
Each change is an **edit command** (set value, insert row, delete range, merge cells, hide/show rows, create table, sort, apply filter, set validation, remove duplicates, text-to-columns…). Commands are the unit of undo/redo: the model mutation lives in Core; the *stacks* live in the app's `UndoRedoManager` (session-only, capped at 1,000 steps per `PRD.md` §8.5). (Applying/clearing a filter changes only row visibility, not data.)

### Reading & writing files
- **`.ods` is a ZIP of XML.** `OdsPackage` handles the archive. The `mimetype` entry must be the **first** entry and **stored uncompressed** — special-cased.
- Package parts: `content.xml`, `styles.xml`, `meta.xml`, `settings.xml` (active sheet, zoom, frozen panes, calc mode), the manifest, and `mimetype`.
- **Tables/sort/filter** ↔ ODF `table:database-range` (with `table:sort` and `table:filter`); **data validation** ↔ `table:content-validation`; **hidden rows/columns** ↔ `table:visibility`; **hyperlinks** ↔ `text:a`. These are native ODF, so they round-trip with LibreOffice. Excel structured references are deliberately not emitted (`PRD.md` §9).
- **Unsupported content is preserved verbatim** (`PRD.md` §8.1): unmodeled elements are kept on read and re-emitted on write.
- **OpenFormula translation:** formulas are stored canonically (`of:=SUM([.A1:.A3])`), not as typed. `OpenFormulaTranslator` converts both ways. Skipping it corrupts formulas on round-trip.
- **CSV** (`PRD.md` §8.4): UTF-8 (BOM-tolerant), configurable delimiter (comma default), RFC 4180 quoting, LF on export; export writes computed values, import infers number/date by locale.

## 6. The custom grid control (the heart of the app)

Real spreadsheets draw their own grid; so does Cellestra — and Avalonia 12's faster pipeline helps.

- **`SpreadsheetGrid`** draws only **visible** cells (virtualization).
- It reads cell data **directly from the worksheet** — no per-cell ViewModels.
- A small **inline editor** (text box over the active cell) gives proper text input, selection, and IME.
- Helpers handle **layout** (skipping hidden/filtered rows and columns), **selection**, **input**, **fill handle**, **merged regions**, **zoom**, **frozen panes**, **table chrome** (banded rows + header filter buttons), and the **filter dropdown** popup.
- **Scrolling:** the input handler maps the scroll wheel to vertical movement and **⇧ + wheel to horizontal movement** (trackpads scroll both axes natively); see `PRD.md` §8.9.

MVVM stays for the chrome; the grid's hot path is deliberate direct code.

## 7. The chrome: menu bar, command bar, formula bar, status bar

- **One command catalog.** Every user command is defined once in `AppCommands` (label + description + icon key + default shortcut, plus optional enabled/active predicates). The **native macOS menu bar** (`NativeMenu`), the **adaptive command bar**, the **Command Palette**, and **keybindings** all bind to it — so they cannot drift apart (heuristic #4). Shortcut remaps from Settings override the defaults at this layer.
- **Command Palette** (`PRD.md` §6.7) — a `CommandPalette` overlay that fuzzy-matches the `AppCommands` catalog (⌘K) and runs the chosen command against the current selection. Since it reads the same catalog, its rows automatically carry the right icon, label, description, shortcut, and enabled/disabled state.
- **Sheet switcher** (`PRD.md` §6.7) — a similar overlay that fuzzy-matches the open workbook's worksheet list (⌘⇧P) and activates the chosen sheet, data-backed by the `Workbook`, not the command catalog.
- **Adaptive command bar** — a `CommandBar` control (`Controls/CommandBar`) laid out by `CommandBarModel` per `PRD.md` §6.3: one always-present row of task groups, each with primary icon tools and an overflow `CommandBarDrawer` (labelled rows plus the Text group's font-size stepper and colour swatches with live preview). Context-aware (disabled/lit per the catalog's predicates) and collapsible (collapsed state persisted in settings). Icons render via `Controls/Icons` from local Lucide SVGs.
- **Formula bar** — Name Box (jump-to-cell/named range), accept/cancel/`fx`, and the formula input, bound to `FormulaBarViewModel` + current selection.
- **Status bar** — `StatusBarViewModel`: active cell, selection stats (Count always; Sum/Average when numeric cells are selected), calc-mode badge, busy indicator, dirty flag, zoom.
- **Function picker, autocomplete, and Function Reference** all read the one `FunctionRegistry`.
- **Data dialogs** — Sort (multi-level), Data Validation, Text to Columns, and Remove Duplicates are modal views over Core's table/sort/filter/validation engines; the per-column filter dropdown is part of the grid (§6).

## 8. How the app talks to the brain (Services)

- **Workbook file service** — async load/save `.ods` via Core (with progress + cancellation).
- **Undo/redo manager** — holds the stacks (session-only, 1,000-step cap); drives Core's edit commands.
- **Clipboard service** — copy/paste as tab-separated text to/from other apps; backs Paste Special.
- **File-dialog & dialog services** — native open/save and modal dialogs, including the confirmations in `PRD.md` §8.7 (the "more than 100 non-empty cells" threshold lives here).
- **Recent-files & settings services** — persisted to `~/Library/Application Support/Cellestra/settings.json` (window state, recent files, shortcut remaps, command-bar collapsed/zoom/view toggles, default calc mode).

## 9. Threading
Recalculation and file I/O run off the UI thread; results are marshaled back. Only the UI thread touches Avalonia controls.

## 10. How the build supports good UX (heuristics + accessibility)

- **Visibility of status** → `StatusBarViewModel` + progress/busy events from the recalc engine and file service.
- **Match real world** → `A1` syntax, familiar function names, human-readable `CellError`.
- **Control & freedom** → `UndoRedoManager`; grid input maps **Esc** to cancel-edit; file ops cancellable.
- **Consistency** → the single `AppCommands` catalog feeding menu, command bar, and keys.
- **Error prevention** → dialog-service confirmations (§8.7 thresholds); number-format tools disabled on text cells; parser validates before an edit commits.
- **Recognition over recall** → command-bar drawers pair each icon with a label + description; the palette surfaces all commands by name; autocomplete, picker, and Reference all read one `FunctionRegistry`; disabled-not-hidden controls.
- **Flexibility & efficiency** → every command in menu + command bar + **Command Palette (⌘K)** + remappable key; **sheet switcher (⌘⇧P)**; full keyboard operation; a collapsible command bar.
- **Minimalist design** → one compact, context-aware command bar instead of a multi-tab ribbon; the palette stands in for always-on discovery chrome; depth behind progressive disclosure (drawers + dialogs); a small flat style set.
- **Diagnose/recover** → `ParseError` carries position + reason; error cells expose their reason on hover.
- **Help** → tooltips from the catalog; Function Reference generated from `FunctionRegistry` metadata.
- **Accessibility** → automation peers (labels/roles) for VoiceOver; full keyboard operation; honors system text-scaling/reduced-motion/increased-contrast; the renderer draws clear focus indicators.

Most UX guarantees are **backed by structure** (one command catalog → three surfaces; one registry → evaluation, autocomplete, help), not bolted on.

## 11. Key decisions & tradeoffs

| Decision | Why | Tradeoff |
|---|---|---|
| Core has zero UI dependencies | Fast tests; swappable UI | A little extra plumbing |
| Custom-drawn grid, no per-cell ViewModels | Speed & memory at scale | Grid behavior is built by hand |
| C# + Avalonia 12 over JS/Rust/raylib | Familiar, lean deps, native feel | Not the absolute leanest binary |
| MVVM for chrome, direct code for grid | Right tool per job | Two styles in one codebase (documented) |
| One `AppCommands` catalog → menu+command bar+palette+keys | Consistency | Commands carry metadata |
| Adaptive command bar (collapsible) + Command Palette (⌘K) + sheet switcher (⌘⇧P) | Compact, contextual, keyboard-first | Context-driven tools can shift as the selection changes |
| One `FunctionRegistry` → eval+autocomplete+help | Consistency, less drift | Registry carries metadata |
| Preserve unknown `.ods` content verbatim | Don't destroy user data | More work in the serializer |
| Tables as ODF database ranges, no structured refs | Clean LibreOffice round-trip | No `Table[Column]` formula syntax |
| Bundled local SVG icons (Lucide) | Supply-chain auditability | The set is curated by hand |
| Mac-first | Focus and quality | Other platforms deferred |
| MIT license | Permissive, matches deps | None meaningful |

## 12. Folder structure

The authoritative tree (build to match it):

> **Namespace note (resolved):** the `Workbook/` folder's types use the namespace
> `Cellestra.Core.Model`, **not** `Cellestra.Core.Workbook`. A `Cellestra.Core.Workbook`
> namespace would collide with the root `Workbook` type it contains (C# resolves
> `Workbook.CreateNew()` to the namespace, not the type). The folder name stays
> `Workbook/` per this tree; only the namespace differs. IDE0130 (namespace/folder
> match) is disabled in `.editorconfig` for this reason.

The repository follows a **feature-folder** layout. The repo root holds only
shared, feature-agnostic .NET tooling — `Directory.Build.props`,
`Directory.Packages.props` (the §2.1 pins), `global.json` (SDK 10.0.x),
`nuget.config`, `.editorconfig`, `.gitignore`, `LICENSE`, and `.devcontainer/`.
These are discovered automatically by walking up from each project, so they apply
to every feature. Everything specific to the spreadsheet editor lives under
`features/EditSpreadsheets/`, which is the root of the tree below. Within it the
projects are **flat sibling folders** (the `src/`/`tests/` headings are logical
groupings, not physical directories).

```
features/EditSpreadsheets/          # the spreadsheet-editor feature (the whole app today)
├── Cellestra.slnx                  # the solution
├── README.md
├── AGENTS.md
│
├── src/
│   ├── Cellestra.Core/                      # PURE .NET — no Avalonia
│   │   ├── Cellestra.Core.csproj
│   │   ├── Defaults.cs                       # PRD §8.8 defaults, defined once
│   │   ├── Workbook/
│   │   │   ├── Workbook.cs  Worksheet.cs  Cell.cs
│   │   │   ├── CellAddress.cs            # A1 / sheet-qualified / abs vs rel
│   │   │   ├── CellRange.cs  MergedRegion.cs
│   │   │   ├── Column.cs  Row.cs          # Row/Column carry a Hidden flag
│   │   │   ├── NamedRange.cs  Hyperlink.cs
│   │   │   ├── DocumentSettings.cs       # locale, calc mode, active sheet
│   │   │   └── SheetDimensions.cs
│   │   ├── Values/ (CellValue, CellError, CellValueType)
│   │   ├── Formulas/
│   │   │   ├── Lexing/ (Lexer, Token, TokenType)
│   │   │   ├── Parsing/ (Parser, ParseError)   # position + reason
│   │   │   ├── Ast/ (Expr, LiteralExpr, ReferenceExpr*, RangeExpr,
│   │   │   │        BinaryExpr, UnaryExpr, FunctionCallExpr, IExprVisitor)
│   │   │   ├── Evaluation/ (Evaluator, EvaluationContext, EvaluationResult)
│   │   │   ├── References/ (ReferenceAdjuster)
│   │   │   └── Functions/
│   │   │       ├── IFunction.cs
│   │   │       ├── FunctionMetadata.cs    # name, category, summary, args, example
│   │   │       ├── FunctionRegistry.cs
│   │   │       ├── Math/  Logical/  Text/  Lookup/  DateTime/
│   │   ├── Dependencies/ (DependencyGraph, RecalculationEngine, DirtyTracker,
│   │   │                  CircularReferenceException)
│   │   ├── Styling/ (CellStyle, Font, Fill, Border, Alignment,
│   │   │             NumberFormat, NumberFormatParser, StylePool)
│   │   ├── Data/ (Table, FilterSpec, SortSpec, SortEngine, FilterEngine,
│   │   │          TableAutoExpand, DataValidation, ValidationRule,
│   │   │          DuplicateRemover, TextToColumns)
│   │   ├── Editing/ (IEditCommand, SetCellValueCommand, InsertRowCommand,
│   │   │             DeleteRangeCommand, MergeCellsCommand, SetRowVisibilityCommand,
│   │   │             CreateTableCommand, SortRangeCommand, ApplyFilterCommand,
│   │   │             SetValidationCommand, RemoveDuplicatesCommand, TextToColumnsCommand)
│   │   └── Serialization/
│   │       ├── IWorkbookSerializer.cs
│   │       ├── Csv/ (CsvReader, CsvWriter, CsvOptions)   # PRD §8.4
│   │       └── Ods/
│   │           ├── OdsReader.cs  OdsWriter.cs
│   │           ├── OdsPackage.cs         # mimetype first + stored
│   │           ├── OdsContentXml.cs  OdsStylesXml.cs
│   │           ├── OdsMetaXml.cs  OdsSettingsXml.cs  OdsManifest.cs
│   │           ├── UnknownContentStore.cs   # verbatim-preserved elements (§8.1)
│   │           ├── OpenFormulaTranslator.cs
│   │           └── Mapping/
│   │
│   └── Cellestra.Desktop/                   # the Avalonia 12 app
│       ├── Cellestra.Desktop.csproj
│       ├── Program.cs  App.axaml (+ .cs)  ViewLocator.cs
│       ├── Info.plist                       # bundle id com.cellestra.Cellestra; .ods assoc.
│       ├── Assets/ (Icons/ = local Lucide SVGs, Fonts/)
│       ├── Styles/ (Theme.axaml, Colors.axaml, ChromeStyles.axaml)
│       ├── Commands/ (AppCommands.cs)        # single catalog: label+description+icon+shortcut
│       ├── Menu/ (NativeMenu.axaml)          # macOS menu bar → AppCommands
│       ├── Controls/
│       │   ├── CommandBar/ (CommandBar, CommandBarModel, CommandBarDrawer)
│       │   ├── Icons/ (LucideIcon, LucideIcons)   # local Lucide SVGs → stroked geometry
│       │   ├── SpreadsheetGrid/ (SpreadsheetGrid, SpreadsheetGridRenderer,
│       │   │     CellLayout, GridSelection, GridInputHandler, InlineCellEditor,
│       │   │     FillHandle, MergedRegionMap, ZoomController, FrozenPanes,
│       │   │     GridViewport, TableChrome, FilterDropdown)
│       │   ├── FormulaBar/ (FormulaBar.axaml + .cs, NameBox.cs)
│       │   ├── FormulaAutocomplete.cs        # reads FunctionRegistry metadata
│       │   ├── ColumnHeader.cs  RowHeader.cs
│       ├── Views/
│       │   ├── MainWindow.axaml (+ .cs)  SheetTabStrip  StatusBar
│       │   ├── Dialogs/ (GoToCell, FindReplace, CellFormat, FunctionPicker,
│       │   │     FunctionReference, NameManager, Confirm, Settings, About,
│       │   │     Sort, DataValidation, TextToColumns, RemoveDuplicates, Hyperlink)
│       │   └── Overlays/ (CommandPalette, SheetSwitcher)   # PRD §6.7; FuzzyMatcher.cs
│       ├── ViewModels/ (ViewModelBase, MainWindow, Workbook, Worksheet,
│       │     FormulaBar, Selection, StatusBar, SheetTab,
│       │     CommandPalette, SheetSwitcher, Dialogs/…)
│       ├── Input/ (KeyBindings.axaml)        # keys → AppCommands
│       ├── Converters/ (CellValueToDisplayConverter, …)
│       ├── Diagnostics/ (Logging.cs)
│       └── DependencyInjection/ (ServiceCollectionExtensions.cs)
│   │
│   └── Cellestra.Cli/                       # the `cellestra` console binary — no Avalonia
│       ├── Cellestra.Cli.csproj             # references Core + System.CommandLine only
│       ├── Program.cs  CliApplication.cs     # composes the command tree
│       ├── Infrastructure/ (Workspace.cs = load/recalc/atomic-save preserving §8.1,
│       │     Cells.cs, Output.cs = table|csv|tsv|json, Locale.cs, CliException.cs)
│       └── Commands/ (Options.cs, BaselineCommands.cs, ExtraCommands.cs)
│
├── tests/
│   ├── Cellestra.Core.Tests/             # pure-logic, xUnit v3
│   │   ├── Cellestra.Core.Tests.csproj
│   │   ├── Formulas/ (Lexer, Parser, Evaluator, ReferenceAdjuster, Functions/)
│   │   ├── Dependencies/ (RecalculationTests)
│   │   ├── Styling/   Editing/ (UndoRedoTests)
│   │   ├── Data/ (SortTests, FilterTests, TableAutoExpandTests, ValidationTests)
│   │   ├── Serialization/ (OdsRoundTripTests, OpenFormulaTranslatorTests,
│   │   │                   UnknownContentPreservationTests, CsvTests)
│   │   └── Fixtures/                     # sample .ods + .csv files
│   │
│   ├── Cellestra.UiTests/                # headless UI, xUnit v3
│   │   ├── Cellestra.UiTests.csproj      # Avalonia.Headless.XUnit 12.0.4
│   │   ├── TestAppBuilder.cs             # [assembly: AvaloniaTestApplication(...)]
│   │   ├── SpreadsheetGridTests.cs       # [AvaloniaFact]/[AvaloniaTheory]
│   │   └── CommandBarTests.cs            # command-bar + catalog wiring
│   │
│   ├── Cellestra.Cli.Tests/             # end-to-end CLI, in-process (CliApplication.Run)
│   │   └── CliTests.cs                  # convert/get/set/validate/diff/functions + §8.1 preservation
│   │
│   └── Cellestra.Benchmarks/             # BenchmarkDotNet: recalc & I/O targets (PRD §3)
│
└── docs/ (PRD.md, ARCHITECTURE.md)
```

## 13. Testing strategy

- **Core**, xUnit v3 (`Cellestra.Core.Tests`): lexer, parser, evaluator, every function (vs. expected LibreOffice results), reference adjustment, recalc order, undo/redo, `.ods`/CSV round-trips, and **unknown-content preservation** (§8.1). Milliseconds to run.
- **Round-trip tests** are the safety net for the product promise.
- **Headless UI**, xUnit v3 (`Cellestra.UiTests`): Avalonia 12 headless platform via `Avalonia.Headless.XUnit` 12.0.4, using `[AvaloniaFact]`/`[AvaloniaTheory]` and a one-time `[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]`. Covers grid behavior and that menu/command bar/keys resolve to the same commands.
- **Benchmarks** enforce the §3 speed targets.

> Both test projects are **xUnit v3**. Avalonia's headless integration gained v3 support in Avalonia 12, so staying on 12 keeps the test stack consistent.

## 14. Build & packaging
`dotnet run` for development. `just cellestra-publish-app` (the repo-root `build/package-mac.sh`, shared by all three family apps) produces a notarized macOS `.app` (bundle id `com.cellestra.Cellestra`), registers the `.ods` association via `Cellestra.Desktop/Info.plist`, and signs/notarizes with Apple tooling so it opens files on double-click without warnings.

## 15. Things that will genuinely evolve
- The function library (grows over time).
- Edge cases in verbatim `.ods` preservation as unusual files turn up.
- Whether Core ever gets a second face (different UI or platform).
- Deferred features from `PRD.md` §9 (printing, conditional formatting, sort/filter, etc.).
