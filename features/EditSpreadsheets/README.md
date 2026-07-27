# Cellestra

A clean, fast, native spreadsheet editor for desktop and CLI for editing **OpenDocument Spreadsheet (`.ods`)** files.

> **Name:** *Cellestra* (from **cell** + **orchestra**), pronounced **CELL-estra**.
>
> **Status:** early development, pre-v1.

---

## Why this exists

I wanted a spreadsheet editor that had a modern UI, good keyboard-friendly UX, and a deliberately small dependency tree that I could audit and trust.

Full reasoning is in **[docs/PRD.md](docs/PRD.md)**.

## What it does (v1 goals)

- Open, edit, and save `.ods` files — and round-trip them cleanly with LibreOffice.
- Import/export CSV.
- Multiple sheets, fast scrolling, freeze panes, hide/unhide and insert/delete rows & columns.
- Direct, fully keyboard-driven editing: copy/paste, fill handle, undo/redo, find & replace.
- **Sort, filter, and tables**: a unified **Sort & Filter** dialog that chains multiple sort and filter steps, per-column AutoFilter dropdowns (with active-filter management), and auto-expanding tables (⌘T) with banded styling and an optional header column — plus data validation, hyperlinks, remove-duplicates, and text-to-columns.
- **Chart sheets** (Insert ▸ Chart Sheet): bar/line/pie/area and more, custom-drawn with no third-party charting library, round-tripped as ODF `chart:chart`.
- A VS Code–style **Command Palette** (⌘K) to run any command and a **quick sheet switcher** (⌘⇧P), so the window stays uncluttered.
- Everyday formulas (math, logic, text, lookup, date/time) with cross-sheet and named references, plus automatic recalculation. The formula editor has **point mode** — click or drag in the grid to insert references while typing a formula.
- A custom-drawn grid with freeze panes, smooth sub-cell wheel/trackpad scrolling, overlay scroll bars, the fill handle, and clipped-text indicators.
- Number formats, fonts, colors, borders, and alignment.

Not in v1 (on purpose): pivot tables, macros, cloud sync, and `.xlsx`/Google Sheets as first-class formats. See the [PRD](docs/PRD.md#7-non-goals-deliberately-not-in-v1) for the full list.

The interface is **compact and contextual**: the window opens to the grid, a formula bar with a Name Box, a status bar with live selection stats, and a single **adaptive command bar** — one row of task-grouped icon tools (Edit · Text · Align · Numbers · Insert · Data) with overflow drawers, context-aware enabling, and a collapse handle. The **native macOS menu bar** holds the complete command list, and a **Command Palette** (⌘K) runs any command by name. It's all specified, down to keybindings and iconography, in the [PRD](docs/PRD.md#6-user-interface).

The design is checked against Jakob Nielsen's 10 usability heuristics, and accessibility (VoiceOver, full keyboard operation) is a first-class requirement (see the [PRD](docs/PRD.md#7-user-experience-principles-jakob-nielsens-10-heuristics)).

## Tech at a glance

- **C# on .NET 10**
- **Avalonia 12** for the UI (MVVM for the chrome; a custom-drawn control for the grid)
- A **pure, UI-free core library** holding the data model, formula engine, recalculation, and `.ods`/CSV reading and writing — shared by both the desktop app and the CLI
- A **standalone chart store** (`Cellestra.Charts`): the chart-sheet model and a from-scratch ODF `chart:chart` serializer, with no third-party charting dependency
- A **`cellestra` console tool** (`Cellestra.Cli`) — a third consumer of the core, built on `System.CommandLine`, with **no Avalonia and no network**
- **xUnit v3** for all tests, including headless Avalonia UI tests, plus a **BenchmarkDotNet** harness for the PRD's speed targets

Exact dependency versions are pinned in `Directory.Packages.props`; the canonical list and rationale live in [ARCHITECTURE.md §2.1](docs/ARCHITECTURE.md#21-versions--dependency-pins-single-source-of-truth).

Why these choices (and why not JavaScript, Rust, or raylib) is explained in **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**.

## Requirements

- **macOS** (Apple Silicon or Intel) for the desktop app. Avalonia is cross-platform, so it should build and run on Windows and Linux too — macOS is simply the only target developed and tested so far, so treat the others as untested rather than unsupported by design.
- The **`cellestra` CLI** has no Avalonia dependency, so it runs anywhere .NET 10 does (Linux/CI included).
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- The **Avalonia for VS Code** extension is recommended if you use VS Code — its current release fully supports Avalonia 12 (XAML preview + IntelliSense). JetBrains Rider also has strong built-in Avalonia support.

## Getting started

```bash
# 1. Clone
git clone <your-repo-url> cellestra
cd cellestra

# 2. Restore dependencies
dotnet restore

# 3. Run the app
dotnet run --project Cellestra.Desktop
```

## The `cellestra` command-line tool

`Cellestra.Cli` builds a small, scriptable console tool — `cellestra` — that drives the same Core engine as the desktop app. Because it depends only on Core + `System.CommandLine` (no Avalonia, no network), it runs anywhere .NET 10 does, which makes it handy in CI and build pipelines. Edits are **atomic** and **preserve any unmodeled content in the file verbatim**, exactly like the app's save path.

```bash
# Run it straight from source...
dotnet run --project Cellestra.Cli -- info budget.ods

# ...or build a standalone `cellestra` binary and call it directly
dotnet build Cellestra.Cli
cellestra cat budget.ods --sheet Q3 --range A1:D10
```

A few things it can do:

```bash
cellestra convert data.csv data.ods       # CSV ⇄ .ods, both directions
cellestra get  budget.ods Totals.B7        # read one cell's value (or --formula)
cellestra set  budget.ods Totals.B7 "=SUM(B1:B6)"   # edit and save atomically
cellestra eval "=ROUND(355/113, 4)"        # evaluate a formula with no file at all
cellestra recalc budget.ods --in-place     # refresh cached values
cellestra validate budget.ods              # exit 1 on any #REF!/#VALUE!/circular error
cellestra diff before.ods after.ods        # cell-by-cell diff (exit 1 if they differ)
cellestra stats budget.ods Sheet1.A1:A100  # sum/avg/count/min/max over a range
cellestra find budget.ods "TODO" --regex   # search values and/or formulas
cellestra functions VLOOKUP                # describe a built-in function
```

That's the core; the full surface also covers `new`/`import`/`export`, batch edits from JSON (`apply`), sheet management, the desktop Data/Insert edits (`sort`, `remove-duplicates`, `split`, `table`, `validation`, `name`, `hyperlink`, insert/delete/hide/show rows & columns, `merge`/`unmerge`), `chart` sheets, opening a file in the GUI (`open`), and shell `completions`. Run `cellestra --help`, or `cellestra <command> --help`, for the details.

## Running the tests

```bash
# All tests
dotnet test

# Just the core logic (fast, no UI)
dotnet test Cellestra.Core.Tests

# Just the headless UI tests
dotnet test Cellestra.UiTests

# Just the CLI tests (drive the command surface in-process)
dotnet test Cellestra.Cli.Tests
```

Both test projects use **xUnit v3**. The headless UI tests run on Avalonia 12's in-memory headless platform (`Avalonia.Headless.XUnit`), using `[AvaloniaFact]`/`[AvaloniaTheory]` instead of the usual xUnit attributes. The core library is where most of the important logic — and most of the tests — live; the round-trip tests are the safety net for "save a file, reopen it in LibreOffice, lose nothing."

## Running the benchmarks

The `Cellestra.Benchmarks` project is a [BenchmarkDotNet](https://benchmarkdotnet.org/) harness that guards the PRD §3 speed targets (file open, scroll, recalc). Run it in Release:

```bash
# Run every benchmark
dotnet run -c Release --project Cellestra.Benchmarks

# ...or pick one interactively / by filter
dotnet run -c Release --project Cellestra.Benchmarks -- --filter '*Recalc*'
```

## Building a macOS app bundle

```bash
just publish-cellestra-app        # from the repo root — artifacts/cellestra/<rid>/Cellestra.app
just install-cellestra-app        # ...and copy it into /Applications
```

This produces a notarized `.app` you can launch without security warnings. The packaging script is
shared by all three family apps (`build/package-mac.sh cellestra` runs it directly); Cellestra's own
bundle metadata lives in `Cellestra.Desktop/Info.plist`. (See [ARCHITECTURE.md](docs/ARCHITECTURE.md#13-build--packaging).)

## Project layout (quick map)

```
features/EditSpreadsheets/   # this feature; one folder per .NET project
  Cellestra.Core/            # the brain: model, formulas, recalc, .ods + CSV  (no UI)
  Cellestra.Charts/          # chart-sheet model + from-scratch ODF chart serializer
  Cellestra.Editor/          # embeddable Avalonia editor cluster (grid, command bar, dialogs)
  Cellestra.Desktop/         # the face: Avalonia window host wrapping the editor
  Cellestra.Cli/             # the `cellestra` console tool  (Core + System.CommandLine)
  Cellestra.Core.Tests/      # fast unit tests for the brain (xUnit v3)
  Cellestra.UiTests/         # headless UI tests (xUnit v3 + Avalonia.Headless)
  Cellestra.Cli.Tests/       # in-process tests of the CLI command surface
  Cellestra.Benchmarks/      # BenchmarkDotNet speed checks for recalc and file I/O
  Cellestra.slnx             # the solution
  build/                     # macOS packaging
  docs/
    PRD.md                   # what & why
    ARCHITECTURE.md          # how
```

Shared, feature-agnostic .NET tooling stays at the repository root: `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `nuget.config`, `.editorconfig`, and `.devcontainer/`.

The full, annotated tree is in [ARCHITECTURE.md](docs/ARCHITECTURE.md#11-folder-structure).

## A note on dependencies

Part of the point of Cellestra is owning the supply chain. Keep the dependency list short and explainable: prefer the .NET standard library (it already handles ZIP and XML for `.ods`), and add packages only when the reason is obvious. New dependencies are a deliberate decision, not a reflex.

## Documentation

- **[docs/PRD.md](docs/PRD.md)** — the *what* and *why*: problem, users, features, flows, UX principles, success criteria, and explicit non-goals. Changes slowly.
- **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** — the *how*: stack, components, data model, key decisions and tradeoffs, folder structure. Evolves as the app is built.

## License

Released under the **[MIT License](LICENSE)**.
