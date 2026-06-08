# Cellestra

A clean, fast, native spreadsheet editor for macOS whose home format is **OpenDocument Spreadsheet (`.ods`)**.

> **Name:** *Cellestra* (from **cell** + **orchestra**), pronounced **cel-LES-trah**.
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
- **Sort, filter, and tables** (⌘T): multi-level sort, per-column AutoFilter, and auto-expanding tables with banded styling — plus data validation, hyperlinks, remove-duplicates, and text-to-columns.
- A VS Code–style **Command Palette** (⌘K) to run any command and a **quick sheet switcher** (⌘⇧P), so the window stays uncluttered.
- Everyday formulas (math, logic, text, lookup, date/time) with cross-sheet and named references, plus automatic recalculation.
- Number formats, fonts, colors, borders, and alignment.

Not in v1 (on purpose): charts, pivot tables, macros, cloud sync, and `.xlsx`/Google Sheets as first-class formats. See the [PRD](docs/PRD.md#7-non-goals-deliberately-not-in-v1) for the full list.

The interface is **compact and contextual**: the window opens to the grid, a formula bar with a Name Box, a status bar with live selection stats, and a single **adaptive command bar** — one row of task-grouped icon tools (Edit · Text · Align · Numbers · Insert · Data) with overflow drawers, context-aware enabling, and a collapse handle. The **native macOS menu bar** holds the complete command list, and a **Command Palette** (⌘K) runs any command by name. It's all specified, down to keybindings and iconography, in the [PRD](docs/PRD.md#6-user-interface).

The design is checked against Jakob Nielsen's 10 usability heuristics, and accessibility (VoiceOver, full keyboard operation) is a first-class requirement (see the [PRD](docs/PRD.md#7-user-experience-principles-jakob-nielsens-10-heuristics)).

## Tech at a glance

- **C# on .NET 10**
- **Avalonia 12** for the UI (MVVM for the chrome; a custom-drawn control for the grid)
- A **pure, UI-free core library** holding the data model, formula engine, recalculation, and `.ods`/CSV reading and writing
- **xUnit v3** for all tests, including headless Avalonia UI tests

Exact dependency versions are pinned in `Directory.Packages.props`; the canonical list and rationale live in [ARCHITECTURE.md §2.1](docs/ARCHITECTURE.md#21-versions--dependency-pins-single-source-of-truth).

Why these choices (and why not JavaScript, Rust, or raylib) is explained in **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**.

## Requirements

- macOS (Apple Silicon or Intel)
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
dotnet run --project src/Cellestra.Desktop
```

## Running the tests

```bash
# All tests
dotnet test

# Just the core logic (fast, no UI)
dotnet test tests/Cellestra.Core.Tests

# Just the headless UI tests
dotnet test tests/Cellestra.UiTests
```

Both test projects use **xUnit v3**. The headless UI tests run on Avalonia 12's in-memory headless platform (`Avalonia.Headless.XUnit`), using `[AvaloniaFact]`/`[AvaloniaTheory]` instead of the usual xUnit attributes. The core library is where most of the important logic — and most of the tests — live; the round-trip tests are the safety net for "save a file, reopen it in LibreOffice, lose nothing."

## Building a macOS app bundle

```bash
./build/package-mac.sh
```

This produces a notarized `.app` you can launch without security warnings. (See [ARCHITECTURE.md](docs/ARCHITECTURE.md#13-build--packaging).)

## Project layout (quick map)

```
src/
  Cellestra.Core/       # the brain: model, formulas, recalc, .ods + CSV  (no UI)
  Cellestra.Desktop/    # the face: Avalonia window, grid, dialogs, services
tests/
  Cellestra.Core.Tests/ # fast unit tests for the brain (xUnit v3)
  Cellestra.UiTests/    # headless UI tests (xUnit v3 + Avalonia.Headless)
  Cellestra.Benchmarks/ # speed checks for recalc and file I/O
build/                  # macOS packaging
docs/
  PRD.md                # what & why
  ARCHITECTURE.md       # how
```

The full, annotated tree is in [ARCHITECTURE.md](docs/ARCHITECTURE.md#11-folder-structure).

## A note on dependencies

Part of the point of Cellestra is owning the supply chain. Keep the dependency list short and explainable: prefer the .NET standard library (it already handles ZIP and XML for `.ods`), and add packages only when the reason is obvious. New dependencies are a deliberate decision, not a reflex.

## Documentation

- **[docs/PRD.md](docs/PRD.md)** — the *what* and *why*: problem, users, features, flows, UX principles, success criteria, and explicit non-goals. Changes slowly.
- **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** — the *how*: stack, components, data model, key decisions and tradeoffs, folder structure. Evolves as the app is built.

## License

Released under the **[MIT License](LICENSE)**.
